using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Modules.Payments.Application;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Payments;

/// <summary>
/// Issue #66: <c>GET /api/reservations/{reservationId}/payments</c> — the
/// resident-facing payment history. A reservation can have 0..N payment
/// attempts (a Rejected/Cancelled one followed by a new one while the hold
/// is still valid), so this is always a list, never a single object.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class PaymentHistoryEndpointTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FakeMercadoPagoClient _fake = new();
    private readonly WebApplicationFactory<Program> _factory;

    private string _ownerEmail = string.Empty;
    private string _neighborEmail = string.Empty;
    private string _adminEmail = string.Empty;

    public PaymentHistoryEndpointTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseSetting("MercadoPago:WebhookSecret", "test-webhook-secret");
            builder.UseSetting("MercadoPago:AccessToken", "TEST-not-a-real-token");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMercadoPagoClient>();
                services.AddSingleton<IMercadoPagoClient>(_fake);
            });
        });
    }

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _ownerEmail = $"payhist-owner-{Guid.NewGuid():N}@example.test";
        _neighborEmail = $"payhist-neighbor-{Guid.NewGuid():N}@example.test";
        _adminEmail = $"payhist-admin-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit1A = await dbContext.Units.SingleAsync(unit => unit.Label == "1A", cancellationToken);
        var unit1B = await dbContext.Units.SingleAsync(unit => unit.Label == "1B", cancellationToken);

        await AddResidentAsync(userManager, dbContext, _ownerEmail, unit1A);
        await AddResidentAsync(userManager, dbContext, _neighborEmail, unit1B);

        var admin = new UserAccount(Guid.NewGuid(), _adminEmail, "Payment History Admin");
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
    public async Task NoPayments_ReturnsEmptyArray_Not404()
    {
        using var owner = await LoginAsync(_ownerEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 7, 1));

        var response = await owner.GetAsync(
            $"/api/reservations/{reservationId}/payments", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<ResidentPaymentResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public async Task CashDeclaration_AppearsWithoutInternalFields()
    {
        using var owner = await LoginAsync(_ownerEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 7, 2));

        var declare = await owner.PostAsync(
            $"/api/reservations/{reservationId}/payments/cash", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, declare.StatusCode);

        var response = await owner.GetAsync(
            $"/api/reservations/{reservationId}/payments", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var items = JsonSerializer.Deserialize<List<ResidentPaymentResponse>>(raw, JsonOptions);
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("Cash", items[0].Method);
        Assert.Equal("Pending", items[0].Status);

        AssertNoInternalFields(raw);
    }

    [Fact]
    public async Task MultipleAttempts_ReturnsAllOfThem_NewestFirst_NeverCollapsedToOne()
    {
        using var owner = await LoginAsync(_ownerEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 7, 3));

        // First attempt: Mercado Pago, later rejected.
        var mercadoPago = await owner.PostAsync(
            $"/api/reservations/{reservationId}/payments/mercadopago",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, mercadoPago.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var firstAttempt = await dbContext.Payments.SingleAsync(
                p => p.ReservationId == reservationId, TestContext.Current.CancellationToken);

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Payments\" SET \"Status\" = 'Rejected' WHERE \"Id\" = {firstAttempt.Id}",
                TestContext.Current.CancellationToken);
        }

        // Second, independent attempt: cash — still allowed since the first
        // is no longer active.
        var declare = await owner.PostAsync(
            $"/api/reservations/{reservationId}/payments/cash", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, declare.StatusCode);

        var response = await owner.GetAsync(
            $"/api/reservations/{reservationId}/payments", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<ResidentPaymentResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        // Newest first: the cash declaration was created after the rejected
        // Mercado Pago attempt.
        Assert.Equal("Cash", items[0].Method);
        Assert.Equal("MercadoPago", items[1].Method);
        Assert.Equal("Rejected", items[1].Status);
    }

    [Fact]
    public async Task NeighborInSameBuilding_Returns403()
    {
        using var owner = await LoginAsync(_ownerEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 7, 4));
        await owner.PostAsync(
            $"/api/reservations/{reservationId}/payments/cash", null, TestContext.Current.CancellationToken);

        using var neighbor = await LoginAsync(_neighborEmail);
        var response = await neighbor.GetAsync(
            $"/api/reservations/{reservationId}/payments", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_CanReadAnyResidentsPaymentHistory()
    {
        using var owner = await LoginAsync(_ownerEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 7, 5));
        await owner.PostAsync(
            $"/api/reservations/{reservationId}/payments/cash", null, TestContext.Current.CancellationToken);

        using var admin = await LoginAsync(_adminEmail);
        var response = await admin.GetAsync(
            $"/api/reservations/{reservationId}/payments", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingReservation_Returns404()
    {
        using var owner = await LoginAsync(_ownerEmail);

        var response = await owner.GetAsync(
            $"/api/reservations/{Guid.NewGuid()}/payments", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApprovedManualReviewCashPayment_ExposesRequiresManualReview_ButNotConfirmingActor()
    {
        using var owner = await LoginAsync(_ownerEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 7, 6));
        var declare = await owner.PostAsync(
            $"/api/reservations/{reservationId}/payments/cash", null, TestContext.Current.CancellationToken);
        var declared = await declare.Content.ReadFromJsonAsync<DeclareResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        // Force the reservation to be Cancelled before the admin confirms
        // receipt, so the payment is Approved for manual review (mirrors the
        // existing PaymentOutcomeRecorder rule — not reimplemented here).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"Reservations\" SET \"Status\" = 'Cancelled', \"CancelledAtUtc\" = {DateTimeOffset.UtcNow} WHERE \"Id\" = {reservationId}",
                    TestContext.Current.CancellationToken);
        }

        using var admin = await LoginAsync(_adminEmail);
        var confirm = await admin.PostAsync(
            $"/api/payments/{declared!.PaymentId}/cash/confirm", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var response = await owner.GetAsync(
            $"/api/reservations/{reservationId}/payments", TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var items = JsonSerializer.Deserialize<List<ResidentPaymentResponse>>(raw, JsonOptions);
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.True(items[0].RequiresManualReview);
        Assert.Equal("ApprovedForCancelledReservation", items[0].ReservationOutcome);

        AssertNoInternalFields(raw);
    }

    private static void AssertNoInternalFields(string raw)
    {
        Assert.DoesNotContain("idempotencyKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkoutUrl", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerOrderId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerStatus", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cashConfirmedByUserId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("webhookSecret", raw, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> CreateReservationAsync(HttpClient client, DateOnly date)
    {
        var start = new DateTimeOffset(
            date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(-3)).ToUniversalTime();

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            new
            {
                buildingId = DevelopmentDataSeeder.PilotBuildingId,
                amenityId = DevelopmentDataSeeder.PilotSumId,
                useType = "SharedLeisure",
                startsAtUtc = start,
                endsAtUtc = start.AddHours(1)
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedReservation>(
            JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
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

    private static async Task AddResidentAsync(
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        string email,
        Unit unit)
    {
        var user = new UserAccount(Guid.NewGuid(), email, email);
        Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, ApplicationRoles.Resident)).Succeeded);

        dbContext.ResidentMemberships.Add(new ResidentMembership(
            Guid.NewGuid(), unit.BuildingId, unit.Id, user.Id, DateTimeOffset.UtcNow));
    }

    private sealed record CreatedReservation(Guid Id);

    private sealed record DeclareResponse(Guid PaymentId, string Status, DateTimeOffset ReservationExpiresAtUtc);

    private sealed record ResidentPaymentResponse(
        Guid PaymentId,
        Guid ReservationId,
        string Method,
        string Status,
        decimal Amount,
        string Currency,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ApprovedAtUtc,
        string ReservationOutcome,
        bool RequiresManualReview,
        DateTimeOffset? CashConfirmedAtUtc);
}
