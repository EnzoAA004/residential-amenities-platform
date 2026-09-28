using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
/// End-to-end payment flow against real PostgreSQL with a fake Mercado Pago
/// (no network, no real credentials).
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class PaymentFlowTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private const string WebhookSecret = "test-webhook-secret";
    private const string AccessToken = "TEST-not-a-real-token";
    private static readonly TimeSpan BuildingOffset = TimeSpan.FromHours(-3);

    private readonly FakeMercadoPagoClient _fake = new();
    private readonly WebApplicationFactory<Program> _factory;

    private string _residentEmail = string.Empty;
    private string _neighborEmail = string.Empty;
    private string _outsiderEmail = string.Empty;

    public PaymentFlowTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseSetting("MercadoPago:WebhookSecret", WebhookSecret);
            builder.UseSetting("MercadoPago:AccessToken", AccessToken);

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

        _residentEmail = $"pay-resident-{Guid.NewGuid():N}@example.test";
        _neighborEmail = $"pay-neighbor-{Guid.NewGuid():N}@example.test";
        _outsiderEmail = $"pay-outsider-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit1A = await dbContext.Units.SingleAsync(unit => unit.Label == "1A", cancellationToken);
        var unit1B = await dbContext.Units.SingleAsync(unit => unit.Label == "1B", cancellationToken);

        await AddResidentAsync(userManager, dbContext, _residentEmail, unit1A, withMembership: true);
        await AddResidentAsync(userManager, dbContext, _neighborEmail, unit1B, withMembership: true);
        await AddResidentAsync(userManager, dbContext, _outsiderEmail, unit1A, withMembership: false);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    // --- initiating a payment ----------------------------------------------

    [Fact]
    public async Task Initiate_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/reservations/{Guid.NewGuid()}/payments/mercadopago",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Initiate_ResidentWithoutAccessToTheReservationsBuilding_Returns403()
    {
        using var owner = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 8, 1));

        using var outsider = await LoginAsync(_outsiderEmail);
        var response = await InitiateAsync(outsider, reservationId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task Initiate_UnknownReservation_Returns404()
    {
        using var client = await LoginAsync(_residentEmail);

        var response = await InitiateAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Initiate_ExpiredReservation_DoesNotStartCheckout()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 8, 2));
        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await InitiateAsync(client, reservationId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountPaymentsAsync(reservationId));
        Assert.DoesNotContain(
            _fake.CreateCalls,
            call => call.Request.Description.Contains(reservationId.ToString("N")));
    }

    [Fact]
    public async Task Initiate_PendingReservation_CreatesPaymentWithSnapshotAmountAndProviderCheckoutUrl()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 8, 3));

        var response = await InitiateAsync(client, reservationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(AccessToken, raw);
        Assert.DoesNotContain(WebhookSecret, raw);

        var body = JsonSerializer.Deserialize<InitiateResponse>(raw, JsonOptions)!;
        Assert.StartsWith("https://fake.mercadopago.test/checkout", body.CheckoutUrl);
        Assert.False(string.IsNullOrWhiteSpace(body.ProviderOrderId));

        var payment = await LoadPaymentAsync(body.PaymentId);
        Assert.Equal(5_000m, payment.Amount); // SUM shared-leisure price snapshot
        Assert.Equal("ARS", payment.Currency);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(body.ProviderOrderId, payment.ProviderOrderId);

        var reservation = await LoadReservationAsync(reservationId);
        Assert.Equal(reservation.ExpiresAtUtc, body.ReservationExpiresAtUtc, TimeSpan.FromMilliseconds(1));

        var call = _fake.CreateCalls.Single(c => c.IdempotencyKey == payment.IdempotencyKey);
        Assert.Equal(payment.ExternalReference, call.Request.ExternalReference);
        Assert.Equal(5_000m, call.Request.TotalAmount);
    }

    [Fact]
    public async Task Initiate_ExpirationTime_IsDerivedFromRemainingHold_NotHardcoded()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 8, 4));
        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(7));

        var response = await InitiateAsync(client, reservationId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payment = await LoadPaymentByReservationAsync(reservationId);
        var duration = System.Xml.XmlConvert.ToTimeSpan(payment.RequestedExpirationTime!);

        Assert.InRange(duration.TotalSeconds, 6 * 60, 7 * 60);
    }

    [Fact]
    public async Task Initiate_AmountComesFromSnapshot_NotFromCurrentPriceRules()
    {
        var amenityId = Guid.NewGuid();
        await CreateDedicatedAmenityWithRuleAsync(amenityId, 4_000m);

        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(
            client, new DateOnly(2027, 8, 5), amenityId);

        // The price rule changes AFTER the reservation snapshot was taken.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rule = await dbContext.PriceRules.SingleAsync(
                candidate => candidate.AmenityId == amenityId,
                TestContext.Current.CancellationToken);
            var now = DateTimeOffset.UtcNow;
            rule.Supersede(now);
            dbContext.PriceRules.Add(new PriceRule(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotBuildingId,
                amenityId,
                PriceComponentType.Base,
                ReservationUseType.SharedLeisure,
                "ARS",
                9_999m,
                now,
                null));
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var response = await InitiateAsync(client, reservationId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payment = await LoadPaymentByReservationAsync(reservationId);
        Assert.Equal(4_000m, payment.Amount);
    }

    [Fact]
    public async Task Initiate_TechnicalRetry_DoesNotCreateDuplicatePaymentOrOrder()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 8, 6));
        var ordersBefore = _fake.DistinctOrdersCreated;

        var first = await ReadInitiateAsync(await InitiateAsync(client, reservationId));
        var second = await ReadInitiateAsync(await InitiateAsync(client, reservationId));

        Assert.Equal(first.PaymentId, second.PaymentId);
        Assert.Equal(first.ProviderOrderId, second.ProviderOrderId);
        Assert.Equal(1, await CountPaymentsAsync(reservationId));
        Assert.Equal(ordersBefore + 1, _fake.DistinctOrdersCreated);
    }

    [Fact]
    public async Task Initiate_LostProviderResponse_RetryReusesTheSameIdempotencyKeyAndBody()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 8, 7));
        var ordersBefore = _fake.DistinctOrdersCreated;

        // The provider creates the order, but we never see the response.
        _fake.LoseNextCreateResponse = true;
        var failed = await InitiateAsync(client, reservationId);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);

        var afterFailure = await LoadPaymentByReservationAsync(reservationId);
        Assert.Equal(PaymentStatus.Created, afterFailure.Status);
        Assert.Null(afterFailure.ProviderOrderId);

        var retry = await InitiateAsync(client, reservationId);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        var calls = _fake.CreateCalls
            .Where(call => call.Request.ExternalReference == afterFailure.ExternalReference)
            .ToList();

        Assert.Equal(2, calls.Count);
        Assert.Equal(afterFailure.IdempotencyKey, calls[0].IdempotencyKey);
        Assert.Equal(calls[0].IdempotencyKey, calls[1].IdempotencyKey);
        Assert.Equal(calls[0].Request, calls[1].Request); // identical body incl. expiration_time
        Assert.Equal(ordersBefore + 1, _fake.DistinctOrdersCreated); // no duplicate remote order
        Assert.Equal(1, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task Initiate_ConcurrentRequests_ProduceASingleAttemptAndOrder()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 8, 8));
        var ordersBefore = _fake.DistinctOrdersCreated;

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => InitiateAsync(client, reservationId)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var results = await Task.WhenAll(responses.Select(ReadInitiateAsync));
        Assert.Single(results.Select(r => r.PaymentId).Distinct());
        Assert.Single(results.Select(r => r.ProviderOrderId).Distinct());
        Assert.Equal(1, await CountPaymentsAsync(reservationId));
        Assert.Equal(ordersBefore + 1, _fake.DistinctOrdersCreated);
    }

    [Fact]
    public async Task Initiate_AfterConfirmation_IsRejected()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 9));
        await ApproveAsync(payment);

        var response = await InitiateAsync(client, reservationId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- webhook: authenticity ---------------------------------------------

    [Fact]
    public async Task Webhook_InvalidSignature_HasNoEffects()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 10));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");
        var getsBefore = _fake.GetCalls;

        // A forged notification claiming approval, signed with the wrong secret.
        var forged = await PostWebhookAsync(payment.ProviderOrderId!, secret: "attacker-secret");
        // Missing headers entirely.
        var unsigned = await PostRawWebhookAsync(payment.ProviderOrderId!, signature: null, requestId: null);

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(getsBefore, _fake.GetCalls); // never even asked the provider
        Assert.Equal(0, await CountEventsAsync(payment.ProviderOrderId!));
        Assert.Equal(PaymentStatus.Pending, (await LoadPaymentAsync(payment.Id)).Status);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(reservationId)).Status);
    }

    [Fact]
    public async Task Webhook_SignedForDifferentOrderId_IsRejected()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 11));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");

        // Signature was valid for some other order; attacker swaps the id.
        var requestId = Guid.NewGuid().ToString();
        var ts = Timestamp();
        var signature = WebhookSigner.Header(WebhookSecret, "ORDSOMEOTHER", requestId, ts);

        var response = await PostRawWebhookAsync(payment.ProviderOrderId!, signature, requestId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(reservationId)).Status);
    }

    // --- webhook: reconciliation ---------------------------------------------

    [Fact]
    public async Task Webhook_Approved_FetchesOrderAndConfirmsReservation()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 12));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");
        var getsBefore = _fake.GetCalls;

        var response = await PostWebhookAsync(payment.ProviderOrderId!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_fake.GetCalls > getsBefore); // server-side fetch happened

        var stored = await LoadPaymentAsync(payment.Id);
        Assert.Equal(PaymentStatus.Approved, stored.Status);
        Assert.Equal("processed", stored.ProviderStatus);
        Assert.Equal("accredited", stored.ProviderStatusDetail);
        Assert.NotNull(stored.ApprovedAtUtc);
        Assert.Equal(PaymentReservationOutcome.ReservationConfirmed, stored.ReservationOutcome);
        Assert.False(stored.RequiresManualReview);

        var reservation = await LoadReservationAsync(reservationId);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.NotNull(reservation.ConfirmedAtUtc);
    }

    [Fact]
    public async Task Webhook_BodyClaimingApproval_IsNotTrusted()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 13));
        // The provider says it is still pending, whatever the notification body claims.
        _fake.SetStatus(payment.ProviderOrderId!, "action_required", "waiting_payment");

        var response = await PostWebhookAsync(
            payment.ProviderOrderId!,
            body: """{"action":"order.processed","type":"order","status":"approved","data":{"id":"x","status":"processed"}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PaymentStatus.Pending, (await LoadPaymentAsync(payment.Id)).Status);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(reservationId)).Status);
    }

    [Fact]
    public async Task Webhook_DuplicateDelivery_DoesNotDuplicateAnyEffect()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 14));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");

        var requestId = Guid.NewGuid().ToString();
        await PostWebhookAsync(payment.ProviderOrderId!, requestId: requestId);

        var firstPayment = await LoadPaymentAsync(payment.Id);
        var firstReservation = await LoadReservationAsync(reservationId);
        var getsAfterFirst = _fake.GetCalls;

        for (var i = 0; i < 9; i++)
        {
            var again = await PostWebhookAsync(payment.ProviderOrderId!, requestId: requestId);
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        var payments = await CountPaymentsAsync(reservationId);
        var lastPayment = await LoadPaymentAsync(payment.Id);
        var lastReservation = await LoadReservationAsync(reservationId);

        Assert.Equal(1, payments);
        Assert.Equal(1, await CountEventsAsync(payment.ProviderOrderId!));
        Assert.Equal(getsAfterFirst, _fake.GetCalls); // duplicates short-circuit before the provider
        Assert.Equal(firstPayment.ApprovedAtUtc, lastPayment.ApprovedAtUtc);
        Assert.Equal(firstReservation.ConfirmedAtUtc, lastReservation.ConfirmedAtUtc);
        Assert.Equal(ReservationStatus.Confirmed, lastReservation.Status);
    }

    [Fact]
    public async Task Webhook_RedeliveredWithNewRequestId_IsStillIdempotent()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 15));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");

        await PostWebhookAsync(payment.ProviderOrderId!);
        var first = await LoadPaymentAsync(payment.Id);
        var firstReservation = await LoadReservationAsync(reservationId);

        // Different x-request-id => a different event row, reprocessed from
        // scratch; the business effects must still not repeat.
        await PostWebhookAsync(payment.ProviderOrderId!);
        await PostWebhookAsync(payment.ProviderOrderId!);

        var after = await LoadPaymentAsync(payment.Id);
        var afterReservation = await LoadReservationAsync(reservationId);

        Assert.Equal(first.ApprovedAtUtc, after.ApprovedAtUtc);
        Assert.Equal(firstReservation.ConfirmedAtUtc, afterReservation.ConfirmedAtUtc);
        Assert.Equal(1, await CountPaymentsAsync(reservationId));
        Assert.Equal(PaymentStatus.Approved, after.Status);
    }

    [Fact]
    public async Task Webhook_ConcurrentDuplicates_DoNotDuplicateEffects()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 16));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");

        var requestId = Guid.NewGuid().ToString();
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ =>
                PostWebhookAsync(payment.ProviderOrderId!, requestId: requestId)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await CountEventsAsync(payment.ProviderOrderId!));
        Assert.Equal(PaymentStatus.Approved, (await LoadPaymentAsync(payment.Id)).Status);
        Assert.Equal(ReservationStatus.Confirmed, (await LoadReservationAsync(reservationId)).Status);
        Assert.Equal(1, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task Webhook_AmountMismatch_DoesNotConfirm()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 17));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");
        _fake.Tamper(payment.ProviderOrderId!, totalAmount: 1m);

        var response = await PostWebhookAsync(payment.ProviderOrderId!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PaymentStatus.Pending, (await LoadPaymentAsync(payment.Id)).Status);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(reservationId)).Status);
        Assert.Equal(PaymentEventResult.Mismatch, await LatestEventResultAsync(payment.ProviderOrderId!));
    }

    [Fact]
    public async Task Webhook_CurrencyMismatch_DoesNotConfirm()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 18));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");
        _fake.Tamper(payment.ProviderOrderId!, currency: "USD");

        await PostWebhookAsync(payment.ProviderOrderId!);

        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(reservationId)).Status);
        Assert.Equal(PaymentStatus.Pending, (await LoadPaymentAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task Webhook_ExternalReferenceMismatch_DoesNotConfirm()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 19));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");
        _fake.Tamper(payment.ProviderOrderId!, externalReference: "another-payment");

        await PostWebhookAsync(payment.ProviderOrderId!);

        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(reservationId)).Status);
        Assert.Equal(PaymentStatus.Pending, (await LoadPaymentAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task Webhook_ProviderStillProcessing_KeepsReservationPending()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 20));
        _fake.SetStatus(payment.ProviderOrderId!, "processing", "in_process");

        await PostWebhookAsync(payment.ProviderOrderId!);

        var stored = await LoadPaymentAsync(payment.Id);
        Assert.Equal(PaymentStatus.Pending, stored.Status);
        Assert.Equal("processing", stored.ProviderStatus);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(reservationId)).Status);
    }

    [Fact]
    public async Task Webhook_ProviderFailure_KeepsHoldActive_AndAllowsANewAttempt()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 21));
        _fake.SetStatus(payment.ProviderOrderId!, "failed", "failed");

        await PostWebhookAsync(payment.ProviderOrderId!);

        Assert.Equal(PaymentStatus.Rejected, (await LoadPaymentAsync(payment.Id)).Status);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(reservationId)).Status);

        // A genuinely new attempt (not a technical retry) while the hold lasts.
        var second = await ReadInitiateAsync(await InitiateAsync(client, reservationId));

        Assert.NotEqual(payment.Id, second.PaymentId);
        Assert.NotEqual(payment.ProviderOrderId, second.ProviderOrderId);
        var secondPayment = await LoadPaymentAsync(second.PaymentId);
        Assert.NotEqual(payment.IdempotencyKey, secondPayment.IdempotencyKey);
        Assert.Equal(2, await CountPaymentsAsync(reservationId));
    }

    [Fact]
    public async Task Webhook_UnknownOrder_IsAcknowledgedWithoutEffects()
    {
        var response = await PostWebhookAsync("ORDDOESNOTEXIST0001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_ProviderOutage_ReturnsRetryableError_AndRedeliveryCompletes()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 22));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");
        var requestId = Guid.NewGuid().ToString();

        _fake.FailGets = true;
        var outage = await PostWebhookAsync(payment.ProviderOrderId!, requestId: requestId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, outage.StatusCode);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(reservationId)).Status);

        _fake.FailGets = false;
        var redelivery = await PostWebhookAsync(payment.ProviderOrderId!, requestId: requestId);

        Assert.Equal(HttpStatusCode.OK, redelivery.StatusCode);
        Assert.Equal(ReservationStatus.Confirmed, (await LoadReservationAsync(reservationId)).Status);
    }

    // --- payment vs expiration ------------------------------------------------

    [Fact]
    public async Task PaymentWinsBeforeExpiration_ReservationConfirmed_AndExpirationJobLeavesItAlone()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 23));
        await ApproveAsync(payment);

        // Even if its (now irrelevant) hold deadline passes, the job must not touch it.
        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(-5));
        await ExpirePastHoldsAsync();

        var reservation = await LoadReservationAsync(reservationId);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Null(reservation.ExpiredAtUtc);
    }

    [Fact]
    public async Task ExpirationWinsFirst_LatePaymentIsRecorded_ButReservationIsNeverRevived()
    {
        using var client = await LoginAsync(_residentEmail);
        var date = new DateOnly(2027, 8, 24);
        var (reservationId, payment) = await StartPaymentAsync(client, date);

        // The hold lapses and the job releases the resources.
        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(-1));
        await ExpirePastHoldsAsync();
        Assert.Equal(ReservationStatus.Expired, (await LoadReservationAsync(reservationId)).Status);

        // Someone else legitimately books the freed slot (exclusive: would conflict if revived).
        using var neighbor = await LoginAsync(_neighborEmail);
        var neighborReservationId = await CreateReservationAsync(
            neighbor, date, useType: "ExclusiveLeisure");

        // Mercado Pago only now reports the money as credited.
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");
        var response = await PostWebhookAsync(payment.ProviderOrderId!);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await LoadPaymentAsync(payment.Id);
        Assert.Equal(PaymentStatus.Approved, stored.Status); // provider truth is recorded
        Assert.Equal(PaymentReservationOutcome.ApprovedAfterExpiry, stored.ReservationOutcome);
        Assert.True(stored.RequiresManualReview);

        var expired = await LoadReservationAsync(reservationId);
        Assert.Equal(ReservationStatus.Expired, expired.Status);
        Assert.Null(expired.ConfirmedAtUtc);

        // The neighbor's booking is untouched and still holds the slot.
        Assert.Equal(
            ReservationStatus.Pending,
            (await LoadReservationAsync(neighborReservationId)).Status);
    }

    [Fact]
    public async Task LatePayment_ForPendingReservationPastDeadlineButNotYetSwept_IsAlsoNotConfirmed()
    {
        using var client = await LoginAsync(_residentEmail);
        var (reservationId, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 25));

        // Past the deadline but the expiration job has NOT run yet.
        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(-1));
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");

        await PostWebhookAsync(payment.ProviderOrderId!);

        var stored = await LoadPaymentAsync(payment.Id);
        Assert.Equal(PaymentReservationOutcome.ApprovedAfterExpiry, stored.ReservationOutcome);
        Assert.NotEqual(ReservationStatus.Confirmed, (await LoadReservationAsync(reservationId)).Status);
    }

    [Fact]
    public async Task GetPayment_ReturnsBackendState_AndIsAccessControlled()
    {
        using var client = await LoginAsync(_residentEmail);
        var (_, payment) = await StartPaymentAsync(client, new DateOnly(2027, 8, 26));
        await ApproveAsync(payment);

        var ok = await client.GetAsync(
            $"/api/payments/{payment.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Approved", body);
        Assert.DoesNotContain(AccessToken, body);

        using var outsider = await LoginAsync(_outsiderEmail);
        var forbidden = await outsider.GetAsync(
            $"/api/payments/{payment.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    // --- helpers -----------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record InitiateResponse(
        Guid PaymentId,
        string ProviderOrderId,
        string CheckoutUrl,
        DateTimeOffset ReservationExpiresAtUtc);

    private sealed record CreatedReservation(Guid Id);

    private static async Task<InitiateResponse> ReadInitiateAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InitiateResponse>(
            JsonOptions, TestContext.Current.CancellationToken))!;
    }

    private static Task<HttpResponseMessage> InitiateAsync(HttpClient client, Guid reservationId) =>
        client.PostAsync(
            $"/api/reservations/{reservationId}/payments/mercadopago",
            null,
            TestContext.Current.CancellationToken);

    private async Task<(Guid ReservationId, Payment Payment)> StartPaymentAsync(
        HttpClient client,
        DateOnly date)
    {
        var reservationId = await CreateReservationAsync(client, date);
        var initiated = await ReadInitiateAsync(await InitiateAsync(client, reservationId));
        return (reservationId, await LoadPaymentAsync(initiated.PaymentId));
    }

    private async Task ApproveAsync(Payment payment)
    {
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");
        var response = await PostWebhookAsync(payment.ProviderOrderId!);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string Timestamp() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

    private Task<HttpResponseMessage> PostWebhookAsync(
        string orderId,
        string? secret = null,
        string? requestId = null,
        string? body = null)
    {
        requestId ??= Guid.NewGuid().ToString();
        var signature = WebhookSigner.Header(
            secret ?? WebhookSecret, orderId, requestId, Timestamp());

        return PostRawWebhookAsync(orderId, signature, requestId, body);
    }

    private async Task<HttpResponseMessage> PostRawWebhookAsync(
        string orderId,
        string? signature,
        string? requestId,
        string? body = null)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/webhooks/mercadopago?data.id={Uri.EscapeDataString(orderId)}")
        {
            Content = new StringContent(
                body ?? JsonSerializer.Serialize(new
                {
                    action = "order.processed",
                    type = "order",
                    data = new { id = orderId }
                }),
                Encoding.UTF8,
                "application/json")
        };

        if (signature is not null)
        {
            request.Headers.Add("x-signature", signature);
        }

        if (requestId is not null)
        {
            request.Headers.Add("x-request-id", requestId);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<Guid> CreateReservationAsync(
        HttpClient client,
        DateOnly date,
        Guid? amenityId = null,
        string useType = "SharedLeisure")
    {
        var start = new DateTimeOffset(
            date.ToDateTime(new TimeOnly(10, 0)), BuildingOffset).ToUniversalTime();

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            new
            {
                buildingId = DevelopmentDataSeeder.PilotBuildingId,
                amenityId = amenityId ?? DevelopmentDataSeeder.PilotSumId,
                useType,
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
            $"Payment Snapshot Amenity {amenityId:N}",
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

    private async Task<int> CountEventsAsync(string orderId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.PaymentProviderEvents.CountAsync(
            providerEvent => providerEvent.ProviderOrderId == orderId,
            TestContext.Current.CancellationToken);
    }

    private async Task<PaymentEventResult> LatestEventResultAsync(string orderId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.PaymentProviderEvents
            .Where(providerEvent => providerEvent.ProviderOrderId == orderId)
            .OrderByDescending(providerEvent => providerEvent.ReceivedAtUtc)
            .Select(providerEvent => providerEvent.ProcessingResult)
            .FirstAsync(TestContext.Current.CancellationToken);
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
