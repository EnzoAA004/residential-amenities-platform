using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Application;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Identity;

/// <summary>
/// Issue #93 (DEC-014/OQ-015): resident accounts are Administrator-created
/// only. An Administrator supplies building/unit/email and never learns,
/// stores or sends the resident's final password; the resident verifies a
/// single-use, hashed, expiring, attempt-limited code and sets their own
/// password. The same mechanism backs a later forgotten-password flow with
/// an anti-enumeration response.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class InvitationEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private const string NewPassword = "Br4nd!NewPassw0rd";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

    private string _adminEmail = string.Empty;
    private Guid _buildingId;
    private Guid _unitId;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _adminEmail = $"invite-admin-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var building = new Building(Guid.NewGuid(), "Invitation Building", "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(building);
        _buildingId = building.Id;

        var unit = new Unit(Guid.NewGuid(), building.Id, floor: 1, door: "1", label: "I1");
        dbContext.Units.Add(unit);
        _unitId = unit.Id;

        var admin = new UserAccount(Guid.NewGuid(), _adminEmail, "Invite Admin");
        Assert.True((await userManager.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(admin, ApplicationRoles.Administrator)).Succeeded);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Invite_ByAdmin_CreatesAccountWithNoPassword_AndEmitsAResponseWithoutOne()
    {
        using var admin = await LoginAsync(_adminEmail);
        var email = $"resident-{Guid.NewGuid():N}@example.test";

        var response = await admin.PostAsJsonAsync(
            "/api/admin/residents",
            new { buildingId = _buildingId, unitId = _unitId, email, displayName = "New Resident" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);

        var user = await WithDbAsync(db => db.UserAccounts.AsNoTracking()
            .SingleAsync(u => u.Email == email, TestContext.Current.CancellationToken));
        Assert.Null(user.PasswordHash);
        Assert.False(user.EmailConfirmed);

        // Cannot sign in yet — there is no password to match.
        var loginAttempt = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email, password = "Whatever!123Abc" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, loginAttempt.StatusCode);
    }

    [Fact]
    public async Task Invite_ByResident_Returns403()
    {
        var residentEmail = $"non-admin-{Guid.NewGuid():N}@example.test";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
            var resident = new UserAccount(Guid.NewGuid(), residentEmail, "Not An Admin");
            Assert.True((await userManager.CreateAsync(resident, Password)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(resident, ApplicationRoles.Resident)).Succeeded);
        }

        using var client = await LoginAsync(residentEmail);
        var response = await client.PostAsJsonAsync(
            "/api/admin/residents",
            new { buildingId = _buildingId, unitId = _unitId, email = "x@example.test", displayName = "X" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invite_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/admin/residents",
            new { buildingId = _buildingId, unitId = _unitId, email = "x@example.test", displayName = "X" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invite_UnknownUnit_Returns400()
    {
        using var admin = await LoginAsync(_adminEmail);

        var response = await admin.PostAsJsonAsync(
            "/api/admin/residents",
            new { buildingId = _buildingId, unitId = Guid.NewGuid(), email = "x@example.test", displayName = "X" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invite_DuplicateEmail_Returns400()
    {
        using var admin = await LoginAsync(_adminEmail);
        var email = $"dup-{Guid.NewGuid():N}@example.test";

        var first = await admin.PostAsJsonAsync(
            "/api/admin/residents",
            new { buildingId = _buildingId, unitId = _unitId, email, displayName = "First" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await admin.PostAsJsonAsync(
            "/api/admin/residents",
            new { buildingId = _buildingId, unitId = _unitId, email, displayName = "Second" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Activate_WithCorrectCode_SetsPassword_AndAllowsLogin_AndIsAudited()
    {
        var email = $"activate-{Guid.NewGuid():N}@example.test";
        var userId = await InviteAsync(email);
        var code = await ReadLatestCodeFromLogAsync(email);

        var activateResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/activate",
            new { email, code, newPassword = NewPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, activateResponse.StatusCode);

        var user = await WithDbAsync(db => db.UserAccounts.AsNoTracking()
            .SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken));
        Assert.True(user.EmailConfirmed);
        Assert.NotNull(user.PasswordHash);

        var loginResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email, password = NewPassword },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        Assert.Single(await AuditAsync(userId, AuditAction.ResidentActivated));
    }

    [Fact]
    public async Task Activate_WithWrongCode_Returns400_AndIncrementsAttemptCount()
    {
        var email = $"wrongcode-{Guid.NewGuid():N}@example.test";
        await InviteAsync(email);

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/activate",
            new { email, code = "000000", newPassword = NewPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var verification = await WithDbAsync(db => db.VerificationCodes.AsNoTracking()
            .SingleAsync(v => v.Purpose == VerificationCodePurpose.AccountActivation, TestContext.Current.CancellationToken));
        Assert.Equal(1, verification.AttemptCount);
    }

    [Fact]
    public async Task Activate_AfterExhaustingAttempts_RejectsEvenTheCorrectCode()
    {
        var email = $"exhausted-{Guid.NewGuid():N}@example.test";
        await InviteAsync(email);
        var code = await ReadLatestCodeFromLogAsync(email);
        using var client = _factory.CreateClient();

        for (var i = 0; i < VerificationCode.MaxAttempts; i++)
        {
            var wrong = await client.PostAsJsonAsync(
                "/api/auth/activate",
                new { email, code = "999999", newPassword = NewPassword },
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        var finalAttempt = await client.PostAsJsonAsync(
            "/api/auth/activate",
            new { email, code, newPassword = NewPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, finalAttempt.StatusCode);
    }

    [Fact]
    public async Task Activate_WithExpiredCode_Returns400()
    {
        var email = $"expired-{Guid.NewGuid():N}@example.test";
        var userId = await InviteAsync(email);

        // Replace the freshly-issued code with an equivalent one that is
        // already expired: expiresAtUtc must still be after createdAtUtc
        // (the domain invariant), so both are back-dated together.
        var code = "123456";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = await dbContext.VerificationCodes
                .SingleAsync(v => v.UserId == userId, TestContext.Current.CancellationToken);
            dbContext.VerificationCodes.Remove(existing);

            var nowUtc = DateTimeOffset.UtcNow;
            dbContext.VerificationCodes.Add(new VerificationCode(
                Guid.NewGuid(),
                userId,
                VerificationCodePurpose.AccountActivation,
                VerificationCodeGenerator.Hash(code),
                nowUtc.AddMinutes(-20),
                nowUtc.AddMinutes(-5)));

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/activate",
            new { email, code, newPassword = NewPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReturnsGenericSuccess_AndCreatesNoCode()
    {
        var email = $"unknown-{Guid.NewGuid():N}@example.test";

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/forgot-password", new { email }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.Contains("If an account exists", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ForgotPassword_KnownEmail_ReturnsSameGenericResponse_AndAllowsReset()
    {
        var email = $"forgot-{Guid.NewGuid():N}@example.test";
        var userId = await InviteAsync(email);
        var activationCode = await ReadLatestCodeFromLogAsync(email);
        await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/activate",
            new { email, code = activationCode, newPassword = Password },
            TestContext.Current.CancellationToken);

        var forgotResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/forgot-password", new { email }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);
        var body = await forgotResponse.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.Contains("If an account exists", body.GetProperty("message").GetString());

        var resetCode = await ReadLatestCodeFromLogAsync(email, VerificationCodePurpose.PasswordReset);

        var resetResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/reset-password",
            new { email, code = resetCode, newPassword = NewPassword },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);

        var loginResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email, password = NewPassword },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        Assert.Single(await AuditAsync(userId, AuditAction.PasswordResetRequested));
        Assert.Single(await AuditAsync(userId, AuditAction.PasswordResetCompleted));
    }

    [Fact]
    public async Task ResetPassword_WithWrongCode_Returns400()
    {
        var email = $"resetwrong-{Guid.NewGuid():N}@example.test";
        var activationCode = await ReadLatestCodeFromLogAsync(email, userIdSeed: await InviteAsync(email));
        await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/activate",
            new { email, code = activationCode, newPassword = Password },
            TestContext.Current.CancellationToken);
        await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/forgot-password", new { email }, TestContext.Current.CancellationToken);

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/reset-password",
            new { email, code = "000000", newPassword = NewPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Guid> InviteAsync(string email)
    {
        using var admin = await LoginAsync(_adminEmail);
        var response = await admin.PostAsJsonAsync(
            "/api/admin/residents",
            new { buildingId = _buildingId, unitId = _unitId, email, displayName = "Test Resident" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// The plaintext code is never persisted (only its hash is, and
    /// <see cref="LoggingEmailSender"/> only logs it in this dev/test
    /// environment) — for test purposes, replace the freshly-issued row
    /// with an equivalent one (same purpose, same validity window) built
    /// from a known plaintext code, so the rest of the flow is exercised
    /// exactly as it would be with the real emailed code.
    /// </summary>
    private async Task<string> ReadLatestCodeFromLogAsync(
        string email, VerificationCodePurpose purpose = VerificationCodePurpose.AccountActivation, Guid? userIdSeed = null)
    {
        var userId = userIdSeed ?? await WithDbAsync(db => db.UserAccounts.AsNoTracking()
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync(TestContext.Current.CancellationToken));

        var code = "246810";

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await dbContext.VerificationCodes
            .Where(v => v.UserId == userId && v.Purpose == purpose)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstAsync(TestContext.Current.CancellationToken);
        dbContext.VerificationCodes.Remove(existing);

        var nowUtc = DateTimeOffset.UtcNow;
        dbContext.VerificationCodes.Add(new VerificationCode(
            Guid.NewGuid(),
            userId,
            purpose,
            VerificationCodeGenerator.Hash(code),
            nowUtc,
            nowUtc.AddMinutes(15)));

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return code;
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<List<AuditLog>> AuditAsync(Guid targetId, AuditAction action) =>
        WithDbAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(log => log.TargetId == targetId && log.Action == action)
            .ToListAsync(TestContext.Current.CancellationToken));

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email, password = Password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return client;
    }
}
