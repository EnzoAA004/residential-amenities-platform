using System.Net;
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
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Messaging;

/// <summary>
/// Issue #78: reservation-scoped messaging. A resident sees/writes only
/// messages on their own reservation, never by guessing another resident's
/// reservation id; an Administrator can see/write on any reservation. There
/// is no building-wide channel — every assertion here is scoped to one
/// reservation id.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class ReservationMessagingEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

    private string _ownerEmail = string.Empty;
    private string _otherResidentEmail = string.Empty;
    private string _adminEmail = string.Empty;
    private Guid _reservationId;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _ownerEmail = $"msg-owner-{Guid.NewGuid():N}@example.test";
        _otherResidentEmail = $"msg-other-{Guid.NewGuid():N}@example.test";
        _adminEmail = $"msg-admin-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit1A = await dbContext.Units.SingleAsync(unit => unit.Label == "1A", cancellationToken);
        var unit1B = await dbContext.Units.SingleAsync(unit => unit.Label == "1B", cancellationToken);

        var ownerMembershipId = await AddResidentAsync(userManager, dbContext, _ownerEmail, unit1A);
        await AddResidentAsync(userManager, dbContext, _otherResidentEmail, unit1B);

        var admin = new UserAccount(Guid.NewGuid(), _adminEmail, "Messaging Admin");
        Assert.True((await userManager.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(admin, ApplicationRoles.Administrator)).Succeeded);

        var now = DateTimeOffset.UtcNow;
        var reservation = new Reservation(
            Guid.NewGuid(),
            unit1A.BuildingId,
            ownerMembershipId,
            ReservationUseType.SharedLeisure,
            now.AddDays(1),
            now.AddDays(1).AddHours(1),
            now,
            now.AddMinutes(15));
        dbContext.Reservations.Add(reservation);
        _reservationId = reservation.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/reservations/{_reservationId}/messages",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_UnknownReservation_Returns404()
    {
        using var client = await LoginAsync(_ownerEmail);

        var response = await client.GetAsync(
            $"/api/reservations/{Guid.NewGuid()}/messages",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_NonOwnerResident_Returns403()
    {
        using var client = await LoginAsync(_otherResidentEmail);

        var response = await client.GetAsync(
            $"/api/reservations/{_reservationId}/messages",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_OwnerResident_WithNoMessages_ReturnsEmptyList()
    {
        using var client = await LoginAsync(_ownerEmail);

        var messages = await ListAsync(client, _reservationId);

        Assert.Empty(messages);
    }

    [Fact]
    public async Task Post_OwnerResident_CreatesMessage()
    {
        using var client = await LoginAsync(_ownerEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/reservations/{_reservationId}/messages",
            new { content = "Can I get access to the pool at 10am?" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var message = await response.Content
            .ReadFromJsonAsync<MessageResponse>(JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(message);
        Assert.Equal("Can I get access to the pool at 10am?", message.Content);
        Assert.False(message.AuthorIsAdministrator);

        var messages = await ListAsync(client, _reservationId);
        Assert.Single(messages);
    }

    [Fact]
    public async Task Post_NonOwnerResident_Returns403()
    {
        using var client = await LoginAsync(_otherResidentEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/reservations/{_reservationId}/messages",
            new { content = "Trying to read someone else's reservation." },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_EmptyContent_ReturnsValidationProblem()
    {
        using var client = await LoginAsync(_ownerEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/reservations/{_reservationId}/messages",
            new { content = "   " },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_ContentTooLong_ReturnsValidationProblem()
    {
        using var client = await LoginAsync(_ownerEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/reservations/{_reservationId}/messages",
            new { content = new string('a', 2001) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_CanReadAndPostOnAnyReservation()
    {
        using var ownerClient = await LoginAsync(_ownerEmail);
        await ownerClient.PostAsJsonAsync(
            $"/api/reservations/{_reservationId}/messages",
            new { content = "Owner's first message." },
            TestContext.Current.CancellationToken);

        using var adminClient = await LoginAsync(_adminEmail);

        var postResponse = await adminClient.PostAsJsonAsync(
            $"/api/reservations/{_reservationId}/messages",
            new { content = "Admin reply." },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var messages = await ListAsync(adminClient, _reservationId);
        Assert.Equal(2, messages.Count);
        Assert.False(messages[0].AuthorIsAdministrator);
        Assert.True(messages[1].AuthorIsAdministrator);
    }

    private async Task<List<MessageResponse>> ListAsync(HttpClient client, Guid reservationId)
    {
        var response = await client.GetAsync(
            $"/api/reservations/{reservationId}/messages",
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var messages = await response.Content
            .ReadFromJsonAsync<List<MessageResponse>>(JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(messages);
        return messages;
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email, password = Password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return client;
    }

    private static async Task<Guid> AddResidentAsync(
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        string email,
        Unit unit)
    {
        var user = new UserAccount(Guid.NewGuid(), email, email);
        Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, ApplicationRoles.Resident)).Succeeded);

        var membership = new ResidentMembership(
            Guid.NewGuid(), unit.BuildingId, unit.Id, user.Id, DateTimeOffset.UtcNow);
        dbContext.ResidentMemberships.Add(membership);

        return membership.Id;
    }

    private sealed record MessageResponse(
        Guid Id,
        Guid AuthorUserId,
        string AuthorDisplayName,
        bool AuthorIsAdministrator,
        string Content,
        DateTimeOffset CreatedAtUtc);
}
