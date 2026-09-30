using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Notifications;

[Collection(DevelopmentSeedCollection.Name)]
public sealed class NotificationSubscriptionEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("environment", "Development"));

    private string _residentEmail = string.Empty;
    private string _otherResidentEmail = string.Empty;
    private Guid _residentId;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _residentEmail = $"notifications-resident-{Guid.NewGuid():N}@example.test";
        _otherResidentEmail = $"notifications-other-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();

        var resident = new UserAccount(Guid.NewGuid(), _residentEmail, "Push Resident");
        var other = new UserAccount(Guid.NewGuid(), _otherResidentEmail, "Other Resident");

        Assert.True((await userManager.CreateAsync(resident, Password)).Succeeded);
        Assert.True((await userManager.CreateAsync(other, Password)).Succeeded);
        Assert.True(
            (await userManager.AddToRoleAsync(resident, ApplicationRoles.Resident))
                .Succeeded);
        Assert.True(
            (await userManager.AddToRoleAsync(other, ApplicationRoles.Resident))
                .Succeeded);

        _residentId = resident.Id;
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Register_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/notification-subscriptions",
            ValidRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_AssociatesSubscriptionToAuthenticatedUserOnly()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/notification-subscriptions",
            ValidRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var subscription = await response.Content
            .ReadFromJsonAsync<NotificationSubscriptionResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(subscription);
        Assert.Equal("WebPush", subscription.Platform);
        Assert.True(subscription.IsEnabled);
        Assert.NotEmpty(subscription.EndpointHash);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await dbContext.NotificationSubscriptions
            .SingleAsync(
                candidate => candidate.Id == subscription.Id,
                TestContext.Current.CancellationToken);

        Assert.Equal(_residentId, stored.UserId);
        Assert.Equal("https://push.example.test/subscriptions/abc", stored.Endpoint);
        Assert.Equal("UA test", stored.UserAgent);
    }

    [Fact]
    public async Task Register_UpsertsSameEndpointForSameUser()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var first = await client.PostAsJsonAsync(
            "/api/notification-subscriptions",
            ValidRequest(auth: "auth-1"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "/api/notification-subscriptions",
            ValidRequest(auth: "auth-2", userAgent: "UA refreshed"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await dbContext.NotificationSubscriptions
            .Where(candidate => candidate.UserId == _residentId)
            .ToListAsync(TestContext.Current.CancellationToken);

        var subscription = Assert.Single(stored);
        Assert.Equal("auth-2", subscription.Auth);
        Assert.Equal("UA refreshed", subscription.UserAgent);
    }

    [Fact]
    public async Task Register_InvalidPayload_Returns400AndDoesNotStoreToken()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/notification-subscriptions",
            ValidRequest(endpoint: "http://push.example.test/not-secure"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await dbContext.NotificationSubscriptions.AnyAsync(
            candidate => candidate.UserId == _residentId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Register_CapacitorNativeIsReservedUntilProviderSetupExists()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/notification-subscriptions",
            new RegisterNotificationSubscriptionRequest(
                "CapacitorNative",
                "https://push.example.test/subscriptions/native",
                "p256dh-key",
                "auth-secret",
                "UA test"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyAuthenticatedUsersSafeSubscriptions()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);
        using var otherClient = await CreateAuthenticatedClientAsync(_otherResidentEmail);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                "/api/notification-subscriptions",
                ValidRequest(endpoint: "https://push.example.test/subscriptions/one"),
                TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await otherClient.PostAsJsonAsync(
                "/api/notification-subscriptions",
                ValidRequest(endpoint: "https://push.example.test/subscriptions/two"),
                TestContext.Current.CancellationToken)).StatusCode);

        var subscriptions = await client.GetFromJsonAsync<NotificationSubscriptionResponse[]>(
            "/api/notification-subscriptions",
            TestContext.Current.CancellationToken);

        Assert.NotNull(subscriptions);
        var subscription = Assert.Single(subscriptions);
        Assert.NotEmpty(subscription.EndpointHash);
    }

    [Fact]
    public async Task Unregister_RemovesOnlyTheAuthenticatedUsersSubscription()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);
        using var otherClient = await CreateAuthenticatedClientAsync(_otherResidentEmail);

        var created = await client.PostAsJsonAsync(
            "/api/notification-subscriptions",
            ValidRequest(),
            TestContext.Current.CancellationToken);
        var subscription = await created.Content
            .ReadFromJsonAsync<NotificationSubscriptionResponse>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(subscription);

        var forbiddenDelete = await otherClient.DeleteAsync(
            $"/api/notification-subscriptions/{subscription.Id}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenDelete.StatusCode);

        var delete = await client.DeleteAsync(
            $"/api/notification-subscriptions/{subscription.Id}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await dbContext.NotificationSubscriptions.AnyAsync(
            candidate => candidate.Id == subscription.Id,
            TestContext.Current.CancellationToken));
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
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

    private static RegisterNotificationSubscriptionRequest ValidRequest(
        string endpoint = "https://push.example.test/subscriptions/abc",
        string auth = "auth-secret",
        string userAgent = "UA test") =>
        new("WebPush", endpoint, "p256dh-key", auth, userAgent);

    private sealed record RegisterNotificationSubscriptionRequest(
        string Platform,
        string Endpoint,
        string P256Dh,
        string Auth,
        string? UserAgent);

    private sealed record NotificationSubscriptionResponse(
        Guid Id,
        string Platform,
        string EndpointHash,
        bool IsEnabled,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
