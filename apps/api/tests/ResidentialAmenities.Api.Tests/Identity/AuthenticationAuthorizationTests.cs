using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Identity;

public sealed class AuthenticationAuthorizationTests :
    IAsyncLifetime
{
    private const string Password = "Test!Password123";

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("environment", "Development"));

    private string _residentEmail = string.Empty;
    private string _adminEmail = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        _residentEmail =
            $"resident-{Guid.NewGuid():N}@example.test";

        _adminEmail =
            $"admin-{Guid.NewGuid():N}@example.test";

        await using var scope =
            _factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<UserManager<UserAccount>>();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

        var resident = new UserAccount(
            Guid.NewGuid(),
            _residentEmail,
            "Resident Test");

        var residentResult =
            await userManager.CreateAsync(resident, Password);

        Assert.True(
            residentResult.Succeeded,
            string.Join(
                "; ",
                residentResult.Errors.Select(error => error.Description)));

        var residentRole =
            await userManager.AddToRoleAsync(
                resident,
                ApplicationRoles.Resident);

        Assert.True(residentRole.Succeeded);

        var unit = await dbContext.Units
            .SingleAsync(
                unit => unit.Label == "1A",
                cancellationToken);

        dbContext.ResidentMemberships.Add(
            new ResidentMembership(
                Guid.NewGuid(),
                unit.BuildingId,
                unit.Id,
                resident.Id,
                DateTimeOffset.UtcNow));

        var admin = new UserAccount(
            Guid.NewGuid(),
            _adminEmail,
            "Administrator Test");

        var adminResult =
            await userManager.CreateAsync(admin, Password);

        Assert.True(adminResult.Succeeded);

        var adminRole =
            await userManager.AddToRolesAsync(
                admin,
                [
                    ApplicationRoles.Administrator,
                    ApplicationRoles.Resident
                ]);

        Assert.True(adminRole.Succeeded);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task UnauthenticatedResidentEndpoint_Returns401()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient();

        var response =
            await client.GetAsync(
                "/api/auth/check/resident",
                cancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ResidentCookieLogin_CanUseResidentButNotAdminEndpoint()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                HandleCookies = true
            });

        var login = await LoginAsync(
            client,
            _residentEmail,
            useCookies: true,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var resident =
            await client.GetAsync(
                "/api/auth/check/resident",
                cancellationToken);

        var admin =
            await client.GetAsync(
                "/api/auth/check/admin",
                cancellationToken);

        Assert.Equal(HttpStatusCode.OK, resident.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Fact]
    public async Task AdministratorCookieLogin_CanUseAdminEndpoint()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                HandleCookies = true
            });

        var login = await LoginAsync(
            client,
            _adminEmail,
            useCookies: true,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var response =
            await client.GetAsync(
                "/api/auth/check/admin",
                cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BearerLogin_CanUseResidentEndpointAndRefresh()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient();

        var login = await LoginAsync(
            client,
            _residentEmail,
            useCookies: false,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var loginJson =
            JsonDocument.Parse(
                await login.Content.ReadAsStringAsync(
                    cancellationToken));

        var accessToken =
            loginJson.RootElement
                .GetProperty("accessToken")
                .GetString();

        var refreshToken =
            loginJson.RootElement
                .GetProperty("refreshToken")
                .GetString();

        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        var protectedResponse =
            await client.GetAsync(
                "/api/auth/check/resident",
                cancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            protectedResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;

        var refresh =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken
                },
                cancellationToken);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task CurrentUser_ReturnsActiveMembershipContext()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                HandleCookies = true
            });

        var login = await LoginAsync(
            client,
            _residentEmail,
            useCookies: true,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var response =
            await client.GetAsync(
                "/api/auth/me",
                cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        Assert.Contains(_residentEmail, body);
        Assert.Contains("1A", body);
        Assert.Contains(ApplicationRoles.Resident, body);
    }

    [Fact]
    public async Task PublicRegistrationEndpoint_DoesNotExist()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient();

        var response =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email = "attacker@example.test",
                    password = Password
                },
                cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        bool useCookies,
        CancellationToken cancellationToken)
    {
        return client.PostAsJsonAsync(
            $"/api/auth/login?useCookies={useCookies.ToString().ToLowerInvariant()}",
            new
            {
                email,
                password = Password
            },
            cancellationToken);
    }
}
