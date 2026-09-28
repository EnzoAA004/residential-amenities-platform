using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Modules.Payments.Application;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Application;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Payments;

/// <summary>
/// Cash declaration and authorized confirmation (issue #25: RF-015, RF-016,
/// RB-012) against real PostgreSQL. Mercado Pago is faked and only used to
/// prove the two methods cannot be active on one reservation at once.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class CashPaymentFlowTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly TimeSpan BuildingOffset = TimeSpan.FromHours(-3);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset RaceDeadline = new(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeMercadoPagoClient _fake = new();
    private readonly WebApplicationFactory<Program> _factory;

    private string _residentEmail = string.Empty;
    private string _neighborEmail = string.Empty;
    private string _outsiderEmail = string.Empty;
    private string _adminEmail = string.Empty;
    private Guid _adminId;

    public CashPaymentFlowTests()
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

        _residentEmail = $"cash-resident-{Guid.NewGuid():N}@example.test";
        _neighborEmail = $"cash-neighbor-{Guid.NewGuid():N}@example.test";
        _outsiderEmail = $"cash-outsider-{Guid.NewGuid():N}@example.test";
        _adminEmail = $"cash-admin-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit1A = await dbContext.Units.SingleAsync(unit => unit.Label == "1A", cancellationToken);
        var unit1B = await dbContext.Units.SingleAsync(unit => unit.Label == "1B", cancellationToken);

        await AddResidentAsync(userManager, dbContext, _residentEmail, unit1A, withMembership: true);
        await AddResidentAsync(userManager, dbContext, _neighborEmail, unit1B, withMembership: true);
        await AddResidentAsync(userManager, dbContext, _outsiderEmail, unit1A, withMembership: false);

        var admin = new UserAccount(Guid.NewGuid(), _adminEmail, "Cash Administrator Test");
        Assert.True((await userManager.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await userManager.AddToRolesAsync(
            admin, [ApplicationRoles.Administrator, ApplicationRoles.Resident])).Succeeded);
        _adminId = admin.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    // --- declaring cash ----------------------------------------------------------

    [Fact]
    public async Task Declare_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/reservations/{Guid.NewGuid()}/payments/cash",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Declare_UnknownReservation_Returns404()
    {
        using var client = await LoginAsync(_residentEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await DeclareAsync(client, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Declare_ResidentWithoutMembership_Returns403()
    {
        using var owner = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 10, 1));

        using var outsider = await LoginAsync(_outsiderEmail);
        var response = await DeclareAsync(outsider, reservationId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task Declare_NeighborInSameBuildingWhoDidNotCreateTheReservation_Returns403()
    {
        using var owner = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 10, 2));

        using var neighbor = await LoginAsync(_neighborEmail);
        var response = await DeclareAsync(neighbor, reservationId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task Declare_PendingReservation_CreatesPendingCashPayment_AndLeavesReservationAndHoldUntouched()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 10, 3));
        var before = await LoadReservationAsync(reservationId);

        var response = await DeclareAsync(client, reservationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<DeclareResponse>(
            JsonOptions, TestContext.Current.CancellationToken))!;
        Assert.Equal("Pending", body.Status);

        var payment = await LoadPaymentAsync(body.PaymentId);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.NotNull(payment.CashDeclaredAtUtc);
        Assert.Null(payment.CashConfirmedAtUtc);
        Assert.Null(payment.CashConfirmedByUserId);

        var after = await LoadReservationAsync(reservationId);
        Assert.Equal(ReservationStatus.Pending, after.Status);
        // Declaring cash never extends the hold.
        Assert.Equal(before.ExpiresAtUtc, after.ExpiresAtUtc);
        Assert.Equal(before.ExpiresAtUtc, body.ReservationExpiresAtUtc, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Declare_AdministratorMayDeclareForAResidentsReservation()
    {
        using var owner = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 10, 4));

        using var admin = await LoginAsync(_adminEmail);
        var response = await DeclareAsync(admin, reservationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task Declare_AmountAndCurrencyComeFromTheSnapshot_NotFromCurrentPriceRules()
    {
        var amenityId = Guid.NewGuid();
        await CreateDedicatedAmenityWithRuleAsync(amenityId, 4_000m);

        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(
            client, new DateOnly(2027, 10, 5), amenityId);

        // The price changes after the reservation was quoted.
        await ChangeCurrentPriceAsync(amenityId, 9_999m);

        var declared = await ReadDeclaredAsync(await DeclareAsync(client, reservationId));

        var payment = await LoadPaymentAsync(declared.PaymentId);
        Assert.Equal(4_000m, payment.Amount);
        Assert.Equal("ARS", payment.Currency);
    }

    [Fact]
    public async Task Declare_Twice_ReturnsTheSamePaymentWithoutDuplicating()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 10, 6));

        var first = await ReadDeclaredAsync(await DeclareAsync(client, reservationId));
        var second = await ReadDeclaredAsync(await DeclareAsync(client, reservationId));

        Assert.Equal(first.PaymentId, second.PaymentId);
        Assert.Equal(1, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task Declare_ConcurrentRequests_CreateExactlyOnePayment()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 10, 7));

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => DeclareAsync(client, reservationId)));

        var ids = new HashSet<Guid>();

        foreach (var response in responses)
        {
            ids.Add((await ReadDeclaredAsync(response)).PaymentId);
        }

        Assert.Single(ids);
        Assert.Equal(1, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task Declare_WhenMercadoPagoPaymentIsActive_Returns409AndCreatesNoCashPayment()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 10, 8));

        var mercadoPago = await client.PostAsync(
            $"/api/reservations/{reservationId}/payments/mercadopago",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, mercadoPago.StatusCode);

        var response = await DeclareAsync(client, reservationId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task InitiateMercadoPago_WhenCashPaymentIsActive_Returns409AndCreatesNoProviderOrder()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 10, 9));
        await ReadDeclaredAsync(await DeclareAsync(client, reservationId));
        var createCallsBefore = _fake.CreateCalls.Count;

        var response = await client.PostAsync(
            $"/api/reservations/{reservationId}/payments/mercadopago",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await CountPaymentsAsync(reservationId));
        Assert.Equal(createCallsBefore, _fake.CreateCalls.Count);
    }

    [Fact]
    public async Task Declare_ExpiredReservation_Returns409()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 10, 10));
        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await DeclareAsync(client, reservationId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task Declare_ClientCannotInfluenceAmountStatusOrActor()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 10, 11));

        var response = await client.PostAsJsonAsync(
            $"/api/reservations/{reservationId}/payments/cash",
            new
            {
                amount = 1m,
                currency = "USD",
                status = "Approved",
                confirmedByUserId = Guid.NewGuid()
            },
            TestContext.Current.CancellationToken);

        var declared = await ReadDeclaredAsync(response);
        var payment = await LoadPaymentAsync(declared.PaymentId);

        Assert.Equal(5_000m, payment.Amount);
        Assert.Equal("ARS", payment.Currency);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.CashConfirmedByUserId);
    }

    [Fact]
    public async Task CashPayment_HasNoProviderSpecificData()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 10, 12));
        var declared = await ReadDeclaredAsync(await DeclareAsync(client, reservationId));

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT COUNT(*)::int AS "Value" FROM "Payments"
                WHERE "Id" = {declared.PaymentId}
                  AND "IdempotencyKey" IS NULL AND "RequestedExpirationTime" IS NULL
                  AND "ProviderOrderId" IS NULL AND "CheckoutUrl" IS NULL
                  AND "ProviderStatus" IS NULL AND "ProviderStatusDetail" IS NULL
                """)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, row);
    }

    // --- confirming cash ---------------------------------------------------------

    [Fact]
    public async Task Confirm_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/payments/{Guid.NewGuid()}/cash/confirm",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_AsResident_Returns403_AndChangesNothing()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 13));
        var declared = await ReadDeclaredAsync(await DeclareAsync(resident, reservationId));

        var response = await ConfirmAsync(resident, declared.PaymentId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(PaymentStatus.Pending, (await LoadPaymentAsync(declared.PaymentId)).Status);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(reservationId)).Status);
    }

    [Fact]
    public async Task Confirm_AsAdministrator_ApprovesPayment_AndConfirmsReservation_RecordingTheActor()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 14));
        var declared = await ReadDeclaredAsync(await DeclareAsync(resident, reservationId));

        using var admin = await LoginAsync(_adminEmail);
        var response = await ConfirmAsync(admin, declared.PaymentId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payment = await LoadPaymentAsync(declared.PaymentId);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(_adminId, payment.CashConfirmedByUserId);
        Assert.NotNull(payment.CashConfirmedAtUtc);
        Assert.Equal(PaymentReservationOutcome.ReservationConfirmed, payment.ReservationOutcome);
        Assert.False(payment.RequiresManualReview);

        var reservation = await LoadReservationAsync(reservationId);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.NotNull(reservation.ConfirmedAtUtc);
    }

    [Fact]
    public async Task Confirm_ClientCannotProvideTheConfirmingActor()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 15));
        var declared = await ReadDeclaredAsync(await DeclareAsync(resident, reservationId));

        using var admin = await LoginAsync(_adminEmail);
        var response = await admin.PostAsJsonAsync(
            $"/api/payments/{declared.PaymentId}/cash/confirm?confirmedByUserId={Guid.NewGuid()}",
            new { confirmedByUserId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_adminId, (await LoadPaymentAsync(declared.PaymentId)).CashConfirmedByUserId);
    }

    [Fact]
    public async Task Confirm_Twice_IsIdempotent_AndKeepsTheOriginalActorAndTimestamps()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 16));
        var declared = await ReadDeclaredAsync(await DeclareAsync(resident, reservationId));

        using var admin = await LoginAsync(_adminEmail);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(admin, declared.PaymentId)).StatusCode);
        var first = await LoadPaymentAsync(declared.PaymentId);
        var firstReservation = await LoadReservationAsync(reservationId);

        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(admin, declared.PaymentId)).StatusCode);
        var second = await LoadPaymentAsync(declared.PaymentId);
        var secondReservation = await LoadReservationAsync(reservationId);

        Assert.Equal(PaymentStatus.Approved, second.Status);
        Assert.Equal(first.CashConfirmedByUserId, second.CashConfirmedByUserId);
        Assert.Equal(first.CashConfirmedAtUtc, second.CashConfirmedAtUtc);
        Assert.Equal(first.ApprovedAtUtc, second.ApprovedAtUtc);
        Assert.Equal(first.ReservationOutcome, second.ReservationOutcome);
        Assert.Equal(firstReservation.ConfirmedAtUtc, secondReservation.ConfirmedAtUtc);
        Assert.Equal(ReservationStatus.Confirmed, secondReservation.Status);
    }

    [Fact]
    public async Task Confirm_ConcurrentClicks_ApproveOnce()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 17));
        var declared = await ReadDeclaredAsync(await DeclareAsync(resident, reservationId));

        using var admin = await LoginAsync(_adminEmail);
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => ConfirmAsync(admin, declared.PaymentId)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var payment = await LoadPaymentAsync(declared.PaymentId);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(_adminId, payment.CashConfirmedByUserId);
        Assert.Equal(PaymentReservationOutcome.ReservationConfirmed, payment.ReservationOutcome);
        Assert.Equal(ReservationStatus.Confirmed, (await LoadReservationAsync(reservationId)).Status);
    }

    [Fact]
    public async Task Confirm_UnknownPayment_Returns404()
    {
        using var admin = await LoginAsync(_adminEmail);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ConfirmAsync(admin, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Confirm_MercadoPagoPayment_Returns409()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 18));
        var mercadoPago = await resident.PostAsync(
            $"/api/reservations/{reservationId}/payments/mercadopago",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, mercadoPago.StatusCode);
        var payment = await LoadPaymentByReservationAsync(reservationId);

        using var admin = await LoginAsync(_adminEmail);
        var response = await ConfirmAsync(admin, payment.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(PaymentStatus.Pending, (await LoadPaymentAsync(payment.Id)).Status);
    }

    // --- cash vs. expiration / cancellation -------------------------------------

    [Fact]
    public async Task Confirm_AfterTheReservationExpired_ApprovesPaymentForManualReview_AndDoesNotRevive()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 19));
        var declared = await ReadDeclaredAsync(await DeclareAsync(resident, reservationId));

        // Expiration wins first.
        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(-1));
        await ExpirePastHoldsAsync();
        Assert.Equal(ReservationStatus.Expired, (await LoadReservationAsync(reservationId)).Status);

        using var admin = await LoginAsync(_adminEmail);
        var response = await ConfirmAsync(admin, declared.PaymentId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("ApprovedAfterExpiry", body);

        var payment = await LoadPaymentAsync(declared.PaymentId);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(PaymentReservationOutcome.ApprovedAfterExpiry, payment.ReservationOutcome);
        Assert.True(payment.RequiresManualReview);
        Assert.Equal(_adminId, payment.CashConfirmedByUserId);

        var reservation = await LoadReservationAsync(reservationId);
        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Null(reservation.ConfirmedAtUtc);
    }

    [Fact]
    public async Task Confirm_WhenHoldPassedButExpirationJobHasNotRun_DoesNotConfirm()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 20));
        var declared = await ReadDeclaredAsync(await DeclareAsync(resident, reservationId));
        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(-1));

        using var admin = await LoginAsync(_adminEmail);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(admin, declared.PaymentId)).StatusCode);

        var payment = await LoadPaymentAsync(declared.PaymentId);
        Assert.Equal(PaymentReservationOutcome.ApprovedAfterExpiry, payment.ReservationOutcome);
        Assert.NotEqual(ReservationStatus.Confirmed, (await LoadReservationAsync(reservationId)).Status);
    }

    [Fact]
    public async Task Confirm_ForACancelledReservation_ApprovesPaymentForManualReview_AndDoesNotRevive()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 21));
        var declared = await ReadDeclaredAsync(await DeclareAsync(resident, reservationId));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"Reservations\" SET \"Status\" = 'Cancelled', \"CancelledAtUtc\" = {DateTimeOffset.UtcNow} WHERE \"Id\" = {reservationId}",
                    TestContext.Current.CancellationToken);
        }

        using var admin = await LoginAsync(_adminEmail);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(admin, declared.PaymentId)).StatusCode);

        var payment = await LoadPaymentAsync(declared.PaymentId);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(PaymentReservationOutcome.ApprovedForCancelledReservation, payment.ReservationOutcome);
        Assert.True(payment.RequiresManualReview);
        Assert.Equal(ReservationStatus.Cancelled, (await LoadReservationAsync(reservationId)).Status);
    }

    [Fact]
    public async Task ConfirmationWins_ThenExpirationJob_LeavesReservationConfirmed()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 22));
        var declared = await ReadDeclaredAsync(await DeclareAsync(resident, reservationId));

        using var admin = await LoginAsync(_adminEmail);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(admin, declared.PaymentId)).StatusCode);

        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(-1));
        await ExpirePastHoldsAsync();

        var reservation = await LoadReservationAsync(reservationId);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Null(reservation.ExpiredAtUtc);
    }

    [Fact]
    public async Task ConcurrentCashConfirmationAndExpiration_LeaveAConsistentState()
    {
        const int count = 100;
        var pairs = await InsertPendingReservationsWithCashAsync(count);

        var confirmationClock = new ManualTimeProvider(RaceDeadline.AddSeconds(-1));
        var expirationClock = new ManualTimeProvider(RaceDeadline.AddSeconds(1));
        var actor = Guid.NewGuid();

        var tasks = new List<Task>();

        foreach (var (reservationId, paymentId) in pairs)
        {
            tasks.Add(Task.Run(async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                await NewCashService(db, confirmationClock)
                    .ConfirmAsync(paymentId, actor, TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken));

            tasks.Add(Task.Run(async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();

                await new ReservationExpirationService(
                        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                        expirationClock)
                    .ExpirePastHoldsAsync(TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reservationIds = pairs.Select(pair => pair.ReservationId).ToList();
        var paymentIds = pairs.Select(pair => pair.PaymentId).ToList();

        var reservations = await dbContext.Reservations.AsNoTracking()
            .Where(reservation => reservationIds.Contains(reservation.Id))
            .ToDictionaryAsync(reservation => reservation.Id, TestContext.Current.CancellationToken);
        var payments = await dbContext.Payments.AsNoTracking()
            .Where(payment => paymentIds.Contains(payment.Id))
            .ToDictionaryAsync(payment => payment.Id, TestContext.Current.CancellationToken);

        foreach (var (reservationId, paymentId) in pairs)
        {
            var reservation = reservations[reservationId];
            var payment = payments[paymentId];

            // The cash was confirmed as received either way.
            Assert.Equal(PaymentStatus.Approved, payment.Status);
            Assert.Equal(actor, payment.CashConfirmedByUserId);

            switch (reservation.Status)
            {
                case ReservationStatus.Confirmed:
                    Assert.NotNull(reservation.ConfirmedAtUtc);
                    Assert.Null(reservation.ExpiredAtUtc);
                    Assert.Equal(PaymentReservationOutcome.ReservationConfirmed, payment.ReservationOutcome);
                    Assert.False(payment.RequiresManualReview);
                    break;
                case ReservationStatus.Expired:
                    Assert.Null(reservation.ConfirmedAtUtc);
                    Assert.NotNull(reservation.ExpiredAtUtc);
                    Assert.Equal(PaymentReservationOutcome.ApprovedAfterExpiry, payment.ReservationOutcome);
                    Assert.True(payment.RequiresManualReview);
                    break;
                default:
                    Assert.Fail($"Unexpected final reservation status {reservation.Status}.");
                    break;
            }
        }
    }

    [Fact]
    public async Task CashConfirmationInFlight_WhenExpirationCommitsFirst_DoesNotReviveAndFlagsReview()
    {
        // Deterministic interleaving: a test transaction holds the reservation
        // row lock (an expiration mid-flight). The cash confirmation must wait
        // for it, then observe Expired.
        var (reservationId, paymentId) = (await InsertPendingReservationsWithCashAsync(1)).Single();

        await using var holderScope = _factory.Services.CreateAsyncScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var holderTransaction =
            await holder.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Reservations\" WHERE \"Id\" = {reservationId} FOR UPDATE",
            TestContext.Current.CancellationToken);

        var confirmation = Task.Run(async () =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            return await NewCashService(db, new ManualTimeProvider(RaceDeadline.AddSeconds(-1)))
                .ConfirmAsync(paymentId, Guid.NewGuid(), TestContext.Current.CancellationToken);
        });

        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.False(confirmation.IsCompleted, "confirmation must wait on the reservation row lock");

        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Reservations\" SET \"Status\" = 'Expired', \"ExpiredAtUtc\" = {RaceDeadline.AddSeconds(1)} WHERE \"Id\" = {reservationId}",
            TestContext.Current.CancellationToken);
        await holderTransaction.CommitAsync(TestContext.Current.CancellationToken);

        var payment = await confirmation;

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(PaymentReservationOutcome.ApprovedAfterExpiry, payment.ReservationOutcome);
        Assert.True(payment.RequiresManualReview);
        Assert.Equal(ReservationStatus.Expired, (await LoadReservationAsync(reservationId)).Status);
    }

    // --- reading the payment ----------------------------------------------------

    [Fact]
    public async Task GetPayment_ForCash_ShowsMethodAndConfirmation_WithoutExposingTheAdministratorId()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 10, 23));
        var declared = await ReadDeclaredAsync(await DeclareAsync(resident, reservationId));

        var pending = await resident.GetStringAsync(
            $"/api/payments/{declared.PaymentId}", TestContext.Current.CancellationToken);
        Assert.Contains("\"method\":\"Cash\"", pending);
        Assert.Contains("\"status\":\"Pending\"", pending);

        using var admin = await LoginAsync(_adminEmail);
        await ConfirmAsync(admin, declared.PaymentId);

        var approved = await resident.GetStringAsync(
            $"/api/payments/{declared.PaymentId}", TestContext.Current.CancellationToken);
        Assert.Contains("\"status\":\"Approved\"", approved);
        Assert.Contains("\"reservationOutcome\":\"ReservationConfirmed\"", approved);
        Assert.Contains("\"requiresManualReview\":false", approved);
        Assert.Contains("cashConfirmedAtUtc", approved);
        Assert.DoesNotContain(_adminId.ToString(), approved, StringComparison.OrdinalIgnoreCase);
    }

    // --- helpers -----------------------------------------------------------------

    private sealed record DeclareResponse(
        Guid PaymentId,
        string Status,
        DateTimeOffset ReservationExpiresAtUtc);

    private sealed record CreatedReservation(Guid Id);

    private static CashPaymentService NewCashService(AppDbContext db, TimeProvider clock) =>
        new(
            db,
            new ReservationPaymentContract(db, clock),
            clock,
            NullLogger<CashPaymentService>.Instance);

    private static Task<HttpResponseMessage> DeclareAsync(HttpClient client, Guid reservationId) =>
        client.PostAsync(
            $"/api/reservations/{reservationId}/payments/cash",
            null,
            TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, Guid paymentId) =>
        client.PostAsync(
            $"/api/payments/{paymentId}/cash/confirm",
            null,
            TestContext.Current.CancellationToken);

    private static async Task<DeclareResponse> ReadDeclaredAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeclareResponse>(
            JsonOptions, TestContext.Current.CancellationToken))!;
    }

    private async Task<List<(Guid ReservationId, Guid PaymentId)>> InsertPendingReservationsWithCashAsync(
        int count)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var created = RaceDeadline.AddDays(-1);
        var pairs = new List<(Guid, Guid)>();

        for (var index = 0; index < count; index++)
        {
            var reservation = new Reservation(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ReservationUseType.SharedLeisure,
                RaceDeadline.AddDays(1),
                RaceDeadline.AddDays(1).AddHours(1),
                created,
                RaceDeadline);
            var payment = Payment.CreateCash(
                Guid.NewGuid(), reservation.Id, 5_000m, "ARS", created);

            dbContext.Reservations.Add(reservation);
            dbContext.Payments.Add(payment);
            pairs.Add((reservation.Id, payment.Id));
        }

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return pairs;
    }

    private async Task<Guid> CreateReservationAsync(
        HttpClient client,
        DateOnly date,
        Guid? amenityId = null)
    {
        var start = new DateTimeOffset(
            date.ToDateTime(new TimeOnly(10, 0)), BuildingOffset).ToUniversalTime();

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            new
            {
                buildingId = DevelopmentDataSeeder.PilotBuildingId,
                amenityId = amenityId ?? DevelopmentDataSeeder.PilotSumId,
                useType = "SharedLeisure",
                startsAtUtc = start,
                endsAtUtc = start.AddHours(1)
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreatedReservation>(
            JsonOptions, TestContext.Current.CancellationToken))!.Id;
    }

    private async Task CreateDedicatedAmenityWithRuleAsync(Guid amenityId, decimal amount)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var amenity = new Amenity(
            amenityId,
            DevelopmentDataSeeder.PilotBuildingId,
            $"Cash Snapshot Amenity {amenityId:N}",
            AmenityKind.Other,
            allowsSharedUse: true,
            allowsExclusiveUse: true);

        for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
        {
            amenity.AddAvailabilityWindow(
                Guid.NewGuid(), day, new TimeOnly(9, 0), new TimeOnly(22, 0));
        }

        dbContext.Amenities.Add(amenity);
        dbContext.PriceRules.Add(new PriceRule(
            Guid.NewGuid(),
            DevelopmentDataSeeder.PilotBuildingId,
            amenityId,
            PriceComponentType.Base,
            ReservationUseType.SharedLeisure,
            "ARS",
            amount,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null));

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ChangeCurrentPriceAsync(Guid amenityId, decimal newAmount)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"PriceRules\" SET \"Amount\" = {newAmount} WHERE \"AmenityId\" = {amenityId}",
            TestContext.Current.CancellationToken);
    }

    private async Task SetHoldDeadlineAsync(Guid reservationId, DateTimeOffset deadline)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Reservations\" SET \"ExpiresAtUtc\" = {deadline.ToUniversalTime()} WHERE \"Id\" = {reservationId}",
            TestContext.Current.CancellationToken);
    }

    private async Task ExpirePastHoldsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ReservationExpirationService>();
        await service.ExpirePastHoldsAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Payment> LoadPaymentAsync(Guid paymentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.Payments.AsNoTracking().SingleAsync(
            payment => payment.Id == paymentId,
            TestContext.Current.CancellationToken);
    }

    private async Task<Payment> LoadPaymentByReservationAsync(Guid reservationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.Payments.AsNoTracking().SingleAsync(
            payment => payment.ReservationId == reservationId,
            TestContext.Current.CancellationToken);
    }

    private async Task<int> CountPaymentsAsync(Guid reservationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.Payments.CountAsync(
            payment => payment.ReservationId == reservationId,
            TestContext.Current.CancellationToken);
    }

    private async Task<Reservation> LoadReservationAsync(Guid reservationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.Reservations.AsNoTracking().SingleAsync(
            reservation => reservation.Id == reservationId,
            TestContext.Current.CancellationToken);
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
        Unit unit,
        bool withMembership)
    {
        var user = new UserAccount(Guid.NewGuid(), email, email);
        Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, ApplicationRoles.Resident)).Succeeded);

        if (withMembership)
        {
            dbContext.ResidentMemberships.Add(new ResidentMembership(
                Guid.NewGuid(),
                unit.BuildingId,
                unit.Id,
                user.Id,
                DateTimeOffset.UtcNow));
        }
    }
}
