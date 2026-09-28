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
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
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

namespace ResidentialAmenities.Api.Tests.Audit;

/// <summary>
/// The audit trail (issue #27: RF-021, RNF-007, RB-014) against real
/// PostgreSQL: which business facts are recorded, that idempotent repeats do
/// not duplicate them, that they are atomic with the transition, and the
/// administrative query.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class AuditTrailTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private const string WebhookSecret = "audit-test-webhook-secret";
    private const string AccessToken = "TEST-audit-not-a-real-token";
    private static readonly TimeSpan BuildingOffset = TimeSpan.FromHours(-3);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset RaceDeadline = new(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeMercadoPagoClient _fake = new();
    private readonly WebApplicationFactory<Program> _factory;

    private string _residentEmail = string.Empty;
    private string _adminEmail = string.Empty;
    private Guid _residentId;
    private Guid _adminId;

    public AuditTrailTests()
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

        _residentEmail = $"audit-resident-{Guid.NewGuid():N}@example.test";
        _adminEmail = $"audit-admin-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit1A = await dbContext.Units.SingleAsync(unit => unit.Label == "1A", cancellationToken);

        var resident = new UserAccount(Guid.NewGuid(), _residentEmail, _residentEmail);
        Assert.True((await userManager.CreateAsync(resident, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(resident, ApplicationRoles.Resident)).Succeeded);
        dbContext.ResidentMemberships.Add(new ResidentMembership(
            Guid.NewGuid(), unit1A.BuildingId, unit1A.Id, resident.Id, DateTimeOffset.UtcNow));
        _residentId = resident.Id;

        var admin = new UserAccount(Guid.NewGuid(), _adminEmail, "Audit Administrator Test");
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

    // --- reservations -------------------------------------------------------------

    [Fact]
    public async Task CreatingAReservation_RecordsReservationCreated_WithSafeMetadata()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 11, 1));

        var entry = Assert.Single(await AuditFor(reservationId, AuditAction.ReservationCreated));

        Assert.Equal(AuditActorType.User, entry.ActorType);
        Assert.Equal(_residentId, entry.ActorUserId);
        Assert.Equal(AuditTargetType.Reservation, entry.TargetType);
        Assert.Equal(DevelopmentDataSeeder.PilotBuildingId, entry.BuildingId);
        Assert.False(string.IsNullOrWhiteSpace(entry.CorrelationId));

        using var metadata = JsonDocument.Parse(entry.MetadataJson!);
        Assert.Equal("SharedLeisure", metadata.RootElement.GetProperty("useType").GetString());
        Assert.Equal(1, metadata.RootElement.GetProperty("resourceCount").GetInt32());
        Assert.Equal("Pending", metadata.RootElement.GetProperty("status").GetString());
        Assert.True(metadata.RootElement.TryGetProperty("startsAtUtc", out _));
        Assert.True(metadata.RootElement.TryGetProperty("endsAtUtc", out _));
    }

    [Fact]
    public async Task ARejectedReservation_RecordsNothing()
    {
        using var client = await LoginAsync(_residentEmail);
        var before = await CountAuditAsync(AuditAction.ReservationCreated, _residentId);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            new
            {
                buildingId = DevelopmentDataSeeder.PilotBuildingId,
                amenityId = Guid.NewGuid(),
                useType = "SharedLeisure",
                startsAtUtc = DateTimeOffset.UtcNow.AddDays(400),
                endsAtUtc = DateTimeOffset.UtcNow.AddDays(400).AddHours(1)
            },
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(before, await CountAuditAsync(AuditAction.ReservationCreated, _residentId));
    }

    // --- cash ---------------------------------------------------------------------

    [Fact]
    public async Task DeclaringCash_RecordsCashPaymentDeclared_AndARepeatDoesNot()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 11, 2));

        var first = await ReadPaymentIdAsync(await DeclareCashAsync(client, reservationId));
        var second = await ReadPaymentIdAsync(await DeclareCashAsync(client, reservationId));

        Assert.Equal(first, second);

        var entry = Assert.Single(await AuditFor(first, AuditAction.CashPaymentDeclared));
        Assert.Equal(AuditActorType.User, entry.ActorType);
        Assert.Equal(_residentId, entry.ActorUserId);
        Assert.Equal(AuditTargetType.Payment, entry.TargetType);
        Assert.Equal(DevelopmentDataSeeder.PilotBuildingId, entry.BuildingId);
    }

    [Fact]
    public async Task ConcurrentCashDeclarations_RecordASingleDeclaration()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 11, 3));

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => DeclareCashAsync(client, reservationId)));
        var paymentId = await ReadPaymentIdAsync(responses[0]);

        Assert.Single(await AuditFor(paymentId, AuditAction.CashPaymentDeclared));
    }

    [Fact]
    public async Task ConfirmingCash_RecordsCashPaymentConfirmedAndReservationConfirmed_OnlyOnce()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 11, 4));
        var paymentId = await ReadPaymentIdAsync(await DeclareCashAsync(resident, reservationId));

        using var admin = await LoginAsync(_adminEmail);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmCashAsync(admin, paymentId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmCashAsync(admin, paymentId)).StatusCode);

        var confirmed = Assert.Single(await AuditFor(paymentId, AuditAction.CashPaymentConfirmed));
        Assert.Equal(AuditActorType.User, confirmed.ActorType);
        Assert.Equal(_adminId, confirmed.ActorUserId);

        using var metadata = JsonDocument.Parse(confirmed.MetadataJson!);
        Assert.Equal("Cash", metadata.RootElement.GetProperty("method").GetString());
        Assert.Equal(5_000m, metadata.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("ARS", metadata.RootElement.GetProperty("currency").GetString());
        Assert.Equal(
            "ReservationConfirmed",
            metadata.RootElement.GetProperty("reservationOutcome").GetString());

        // Reservations audits its own transition once, as the system (it never
        // learns whether the payment was cash or Mercado Pago).
        var reservationConfirmed = Assert.Single(
            await AuditFor(reservationId, AuditAction.ReservationConfirmed));
        Assert.Equal(AuditActorType.System, reservationConfirmed.ActorType);
        Assert.Null(reservationConfirmed.ActorUserId);

        Assert.Empty(await AuditFor(paymentId, AuditAction.PaymentRequiresManualReview));
    }

    [Fact]
    public async Task ConcurrentCashConfirmations_RecordASingleConfirmation()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 11, 5));
        var paymentId = await ReadPaymentIdAsync(await DeclareCashAsync(resident, reservationId));

        using var admin = await LoginAsync(_adminEmail);
        await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => ConfirmCashAsync(admin, paymentId)));

        Assert.Single(await AuditFor(paymentId, AuditAction.CashPaymentConfirmed));
        Assert.Single(await AuditFor(reservationId, AuditAction.ReservationConfirmed));
    }

    [Fact]
    public async Task LateCash_RecordsPaymentRequiresManualReview_AndNoReservationConfirmed()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 11, 6));
        var paymentId = await ReadPaymentIdAsync(await DeclareCashAsync(resident, reservationId));

        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(-1));
        await ExpireAsync(DateTimeOffset.UtcNow);

        using var admin = await LoginAsync(_adminEmail);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmCashAsync(admin, paymentId)).StatusCode);

        var review = Assert.Single(await AuditFor(paymentId, AuditAction.PaymentRequiresManualReview));
        Assert.Equal(_adminId, review.ActorUserId);
        using var metadata = JsonDocument.Parse(review.MetadataJson!);
        Assert.Equal("ApprovedAfterExpiry", metadata.RootElement.GetProperty("outcome").GetString());

        Assert.Empty(await AuditFor(reservationId, AuditAction.ReservationConfirmed));
        Assert.Single(await AuditFor(reservationId, AuditAction.ReservationExpired));
    }

    // --- Mercado Pago -------------------------------------------------------------

    [Fact]
    public async Task InitiatingMercadoPago_RecordsTheInitiation_WithoutCheckoutUrlOrKey()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 11, 7));

        var payment = await StartMercadoPagoAsync(client, reservationId);
        await InitiateAsync(client, reservationId); // resumed attempt: not a new initiation

        var entry = Assert.Single(await AuditFor(payment.Id, AuditAction.MercadoPagoPaymentInitiated));
        Assert.Equal(_residentId, entry.ActorUserId);

        Assert.DoesNotContain("checkout", entry.MetadataJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(payment.IdempotencyKey!, entry.MetadataJson!);
    }

    [Fact]
    public async Task MercadoPagoApproval_RecordsPaymentApproved_OnceEvenWhenTheWebhookRepeats()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 11, 8));
        var payment = await StartMercadoPagoAsync(client, reservationId);

        await ApproveAsync(payment);
        await ApproveAsync(payment); // a different delivery of the same fact

        var approved = Assert.Single(await AuditFor(payment.Id, AuditAction.PaymentApproved));
        Assert.Equal(AuditActorType.ExternalProvider, approved.ActorType);
        Assert.Null(approved.ActorUserId);
        Assert.Equal(DevelopmentDataSeeder.PilotBuildingId, approved.BuildingId);

        using var metadata = JsonDocument.Parse(approved.MetadataJson!);
        Assert.Equal("MercadoPago", metadata.RootElement.GetProperty("method").GetString());
        Assert.Equal(
            "ReservationConfirmed",
            metadata.RootElement.GetProperty("reservationOutcome").GetString());

        Assert.Single(await AuditFor(reservationId, AuditAction.ReservationConfirmed));

        // Two distinct deliveries were processed; each is one fact.
        Assert.Equal(
            2,
            (await AuditFor(payment.Id, AuditAction.MercadoPagoWebhookProcessed)).Count);
    }

    [Fact]
    public async Task ARedeliveredWebhookEvent_IsNotAuditedAgain()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 11, 9));
        var payment = await StartMercadoPagoAsync(client, reservationId);
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");

        var requestId = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(payment.ProviderOrderId!, requestId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(payment.ProviderOrderId!, requestId)).StatusCode);

        Assert.Single(await AuditFor(payment.Id, AuditAction.MercadoPagoWebhookProcessed));
        Assert.Single(await AuditFor(payment.Id, AuditAction.PaymentApproved));
    }

    [Fact]
    public async Task LateMercadoPagoApproval_RecordsPaymentRequiresManualReview()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 11, 10));
        var payment = await StartMercadoPagoAsync(client, reservationId);

        await SetHoldDeadlineAsync(reservationId, DateTimeOffset.UtcNow.AddMinutes(-1));
        await ExpireAsync(DateTimeOffset.UtcNow);
        await ApproveAsync(payment);

        var review = Assert.Single(await AuditFor(payment.Id, AuditAction.PaymentRequiresManualReview));
        Assert.Equal(AuditActorType.ExternalProvider, review.ActorType);
        using var metadata = JsonDocument.Parse(review.MetadataJson!);
        Assert.Equal("ApprovedAfterExpiry", metadata.RootElement.GetProperty("outcome").GetString());

        Assert.Single(await AuditFor(payment.Id, AuditAction.PaymentApproved));
        Assert.Empty(await AuditFor(reservationId, AuditAction.ReservationConfirmed));
    }

    [Fact]
    public async Task ProviderRejection_RecordsPaymentRejected_OnlyOnTheRealTransition()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 11, 11));
        var payment = await StartMercadoPagoAsync(client, reservationId);

        _fake.SetStatus(payment.ProviderOrderId!, "failed", "failed");
        await PostWebhookAsync(payment.ProviderOrderId!);
        await PostWebhookAsync(payment.ProviderOrderId!);

        Assert.Single(await AuditFor(payment.Id, AuditAction.PaymentRejected));
        Assert.Empty(await AuditFor(payment.Id, AuditAction.PaymentApproved));
    }

    // --- expiration ---------------------------------------------------------------

    [Fact]
    public async Task Expiration_RecordsReservationExpired_AsTheSystem_AndOnlyOnce()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 11, 12));
        var originalDeadline = DateTimeOffset.UtcNow.AddMinutes(-2);
        await SetHoldDeadlineAsync(reservationId, originalDeadline);

        await ExpireAsync(DateTimeOffset.UtcNow);
        await ExpireAsync(DateTimeOffset.UtcNow); // idempotent second run

        var entry = Assert.Single(await AuditFor(reservationId, AuditAction.ReservationExpired));
        Assert.Equal(AuditActorType.System, entry.ActorType);
        Assert.Null(entry.ActorUserId);
        Assert.Null(entry.CorrelationId);
        Assert.Equal(DevelopmentDataSeeder.PilotBuildingId, entry.BuildingId);

        using var metadata = JsonDocument.Parse(entry.MetadataJson!);
        Assert.Equal(
            originalDeadline,
            metadata.RootElement.GetProperty("originalExpiresAtUtc").GetDateTimeOffset(),
            TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ConcurrentExpirationWorkers_RecordExactlyOneReservationExpiredPerReservation()
    {
        const int count = 60;
        var ids = await InsertPendingReservationsAsync(count);
        var clock = new ManualTimeProvider(RaceDeadline.AddSeconds(1));

        var counts = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                return await TestServices
                    .Expiration(scope.ServiceProvider.GetRequiredService<AppDbContext>(), clock)
                    .ExpirePastHoldsAsync(TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken)));

        // Each reservation was transitioned by exactly one worker...
        Assert.Equal(count, counts.Sum());

        // ...and audited exactly once.
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var perReservation = await db.AuditLogs.AsNoTracking()
            .Where(log =>
                log.Action == AuditAction.ReservationExpired &&
                log.TargetId != null && ids.Contains(log.TargetId.Value))
            .GroupBy(log => log.TargetId)
            .Select(group => group.Count())
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(count, perReservation.Count);
        Assert.All(perReservation, entries => Assert.Equal(1, entries));
    }

    [Fact]
    public async Task ConfirmedReservations_AreNeverAuditedAsExpired()
    {
        var ids = await InsertPendingReservationsAsync(1);
        var id = ids.Single();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await TestServices.Contract(db, new ManualTimeProvider(RaceDeadline.AddSeconds(-1)))
                .ConfirmPaidReservationAsync(id, TestContext.Current.CancellationToken);
        }

        await ExpireAsync(RaceDeadline.AddSeconds(1));

        Assert.Empty(await AuditFor(id, AuditAction.ReservationExpired));
        Assert.Single(await AuditFor(id, AuditAction.ReservationConfirmed));
    }

    // --- atomicity ----------------------------------------------------------------

    [Fact]
    public async Task WhenTheAuditWriteFails_TheConfirmationRollsBackToo()
    {
        var id = (await InsertPendingReservationsAsync(1)).Single();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = new ManualTimeProvider(RaceDeadline.AddSeconds(-1));
            var contract = new ReservationPaymentContract(db, clock, new PoisonedAuditRecorder(db, clock));

            await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                contract.ConfirmPaidReservationAsync(id, TestContext.Current.CancellationToken));
        }

        await using var verify = _factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var reservation = await verifyDb.Reservations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == id, TestContext.Current.CancellationToken);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Null(reservation.ConfirmedAtUtc);
        Assert.False(await verifyDb.AuditLogs.AnyAsync(
            log => log.TargetId == id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhenTheAuditWriteFails_TheExpirationRollsBackToo()
    {
        var id = (await InsertPendingReservationsAsync(1)).Single();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = new ManualTimeProvider(RaceDeadline.AddSeconds(1));
            var service = new ReservationExpirationService(db, clock, new PoisonedAuditRecorder(db, clock));

            await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                service.ExpirePastHoldsAsync(TestContext.Current.CancellationToken));
        }

        await using var verify = _factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var reservation = await verifyDb.Reservations.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == id, TestContext.Current.CancellationToken);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Null(reservation.ExpiredAtUtc);
        Assert.False(await verifyDb.AuditLogs.AnyAsync(
            log => log.TargetId == id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuditLogs_CannotBeModifiedOrDeletedThroughTheApplication()
    {
        using var client = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 11, 13));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entry = await db.AuditLogs.SingleAsync(
            log => log.TargetId == reservationId && log.Action == AuditAction.ReservationCreated,
            TestContext.Current.CancellationToken);

        db.Entry(entry).Property(log => log.MetadataJson).CurrentValue = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));

        db.ChangeTracker.Clear();
        var again = await db.AuditLogs.SingleAsync(
            log => log.Id == entry.Id, TestContext.Current.CancellationToken);
        db.AuditLogs.Remove(again);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));

        db.ChangeTracker.Clear();
        Assert.True(await db.AuditLogs.AnyAsync(
            log => log.Id == entry.Id, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task ThereIsNoWriteEndpointForAudit(string method)
    {
        using var admin = await LoginAsync(_adminEmail);

        var response = await admin.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), "/api/admin/audit"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // --- identity -----------------------------------------------------------------

    [Fact]
    public async Task SuccessfulLogin_RecordsAuthenticationSucceeded()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        using var client = await LoginAsync(_residentEmail);

        var entry = Assert.Single(await RecentAuditAsync(
            AuditAction.AuthenticationSucceeded, before, log => log.ActorUserId == _residentId));

        Assert.Equal(AuditActorType.User, entry.ActorType);
        Assert.Equal(AuditTargetType.User, entry.TargetType);
        Assert.Equal(_residentId, entry.TargetId);
        Assert.Null(entry.BuildingId);
        Assert.DoesNotContain(_residentEmail, entry.MetadataJson ?? string.Empty);
    }

    [Fact]
    public async Task FailedLogin_RecordsAuthenticationFailed_WithoutEmailOrPassword()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var typedEmail = $"nobody-{Guid.NewGuid():N}@example.test";
        const string typedPassword = "Wrong!Password-123-audit";

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email = typedEmail, password = typedPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(typedEmail, body);

        var entries = await RecentAuditAsync(AuditAction.AuthenticationFailed, before, _ => true);
        var entry = entries.First(log => log.MetadataJson!.Contains("InvalidCredentials"));

        Assert.Equal(AuditActorType.System, entry.ActorType);
        Assert.Null(entry.ActorUserId);

        var everything = JsonSerializer.Serialize(
            await AllAuditRowsAsync(), JsonOptions);
        Assert.DoesNotContain(typedEmail, everything);
        Assert.DoesNotContain(typedPassword, everything);
    }

    [Fact]
    public async Task Logout_RecordsTheUser()
    {
        using var client = await LoginAsync(_residentEmail);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await client.PostAsync(
            "/api/auth/logout", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var entry = Assert.Single(await RecentAuditAsync(
            AuditAction.Logout, before, log => log.ActorUserId == _residentId));
        Assert.Equal(AuditTargetType.User, entry.TargetType);
    }

    // --- administrative query ---------------------------------------------------------

    [Fact]
    public async Task AdminAudit_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/admin/audit", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AdminAudit_AsResident_Returns403()
    {
        using var client = await LoginAsync(_residentEmail);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/audit", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AdminAudit_AsAdministrator_ReturnsNewestFirstWithSafeShape()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 11, 14));
        await DeclareCashAsync(resident, reservationId);

        using var admin = await LoginAsync(_adminEmail);
        var page = await GetAuditAsync(admin, $"?targetId={reservationId}");

        var item = Assert.Single(page.Items);
        Assert.Equal("ReservationCreated", item.Action);
        Assert.Equal("User", item.ActorType);
        Assert.Equal(_residentId, item.ActorUserId);
        Assert.Equal("Reservation", item.TargetType);
        Assert.Equal(reservationId, item.TargetId);
        Assert.Equal(DevelopmentDataSeeder.PilotBuildingId, item.BuildingId);
        Assert.Equal("SharedLeisure", item.Metadata.GetProperty("useType").GetString());

        var everything = await GetAuditAsync(admin, "?pageSize=100");
        Assert.Equal(
            everything.Items.OrderByDescending(entry => entry.OccurredAtUtc).Select(entry => entry.Id),
            everything.Items.Select(entry => entry.Id));
    }

    [Fact]
    public async Task AdminAudit_FiltersByBuildingTargetActionAndActor()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 11, 15));
        var paymentId = await ReadPaymentIdAsync(await DeclareCashAsync(resident, reservationId));

        using var admin = await LoginAsync(_adminEmail);

        var otherBuilding = await GetAuditAsync(admin, $"?buildingId={Guid.NewGuid()}");
        Assert.Empty(otherBuilding.Items);

        var byBuilding = await GetAuditAsync(
            admin, $"?buildingId={DevelopmentDataSeeder.PilotBuildingId}&actorUserId={_residentId}&pageSize=100");
        Assert.All(byBuilding.Items, entry =>
            Assert.Equal(DevelopmentDataSeeder.PilotBuildingId, entry.BuildingId));
        Assert.Contains(byBuilding.Items, entry => entry.TargetId == reservationId);

        var byTarget = await GetAuditAsync(admin, $"?targetType=Payment&targetId={paymentId}");
        var declared = Assert.Single(byTarget.Items);
        Assert.Equal("CashPaymentDeclared", declared.Action);

        var byAction = await GetAuditAsync(
            admin, $"?action=CashPaymentDeclared&actorUserId={_residentId}&pageSize=100");
        Assert.All(byAction.Items, entry =>
        {
            Assert.Equal("CashPaymentDeclared", entry.Action);
            Assert.Equal(_residentId, entry.ActorUserId);
        });
        Assert.Contains(byAction.Items, entry => entry.TargetId == paymentId);

        var future = await GetAuditAsync(
            admin, $"?fromUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}");
        Assert.Empty(future.Items);
    }

    [Fact]
    public async Task AdminAudit_UnknownFilterValues_Return400()
    {
        using var admin = await LoginAsync(_adminEmail);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await admin.GetAsync(
                "/api/admin/audit?action=NotAnAction", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await admin.GetAsync(
                "/api/admin/audit?targetType=Nope", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AdminAudit_Paginates_AndCapsThePageSize()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 11, 16));
        var paymentId = await ReadPaymentIdAsync(await DeclareCashAsync(resident, reservationId));

        using var admin = await LoginAsync(_adminEmail);
        await ConfirmCashAsync(admin, paymentId);

        // Reservation created + confirmed; payment declared + confirmed.
        var all = await GetAuditAsync(admin, $"?buildingId={DevelopmentDataSeeder.PilotBuildingId}&actorUserId={_residentId}&pageSize=100");
        Assert.True(all.TotalCount >= 2);

        var first = await GetAuditAsync(
            admin, $"?buildingId={DevelopmentDataSeeder.PilotBuildingId}&pageSize=2&page=1");
        var second = await GetAuditAsync(
            admin, $"?buildingId={DevelopmentDataSeeder.PilotBuildingId}&pageSize=2&page=2");

        Assert.Equal(2, first.Items.Count);
        Assert.Equal(2, first.PageSize);
        Assert.True(first.TotalCount >= 4);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));

        var capped = await GetAuditAsync(admin, "?pageSize=100000");
        Assert.Equal(100, capped.PageSize);
        Assert.True(capped.Items.Count <= 100);
    }

    // --- secrets ------------------------------------------------------------------

    [Fact]
    public async Task NoAuditedRow_ContainsSecretsTokensOrPasswords()
    {
        using var resident = await LoginAsync(_residentEmail);
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2027, 11, 17));
        var payment = await StartMercadoPagoAsync(resident, reservationId);
        await ApproveAsync(payment);

        using var failed = _factory.CreateClient();
        await failed.PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email = _residentEmail, password = "definitely-wrong-password" },
            TestContext.Current.CancellationToken);

        var everything = JsonSerializer.Serialize(await AllAuditRowsAsync(), JsonOptions);

        Assert.DoesNotContain(AccessToken, everything);
        Assert.DoesNotContain(WebhookSecret, everything);
        Assert.DoesNotContain(Password, everything);
        Assert.DoesNotContain("definitely-wrong-password", everything);
        Assert.DoesNotContain(_residentEmail, everything);
        Assert.DoesNotContain("x-signature", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(payment.IdempotencyKey!, everything);
        Assert.DoesNotContain("fake.mercadopago.test", everything);
    }

    // --- helpers ------------------------------------------------------------------

    private sealed class PoisonedAuditRecorder(AppDbContext db, TimeProvider clock) : IAuditRecorder
    {
        // A row PostgreSQL will reject (invalid jsonb) in the very SaveChanges
        // that carries the business change.
        public void Record(AuditRecord record) =>
            db.AuditLogs.Add(new AuditLog(
                Guid.NewGuid(),
                clock.GetUtcNow(),
                record.BuildingId,
                record.ActorType,
                record.ActorUserId,
                record.Action,
                record.TargetType,
                record.TargetId,
                null,
                "this is not json"));
    }

    private sealed record CreatedReservation(Guid Id);

    private sealed record IdResponse(Guid PaymentId);

    private sealed record AuditItem(
        Guid Id,
        DateTimeOffset OccurredAtUtc,
        Guid? BuildingId,
        string ActorType,
        Guid? ActorUserId,
        string Action,
        string TargetType,
        Guid? TargetId,
        string? CorrelationId,
        JsonElement Metadata);

    private sealed record AuditPage(IReadOnlyList<AuditItem> Items, int Page, int PageSize, int TotalCount);

    private static async Task<AuditPage> GetAuditAsync(HttpClient admin, string query)
    {
        var response = await admin.GetAsync(
            "/api/admin/audit" + query, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<AuditPage>(
            JsonOptions, TestContext.Current.CancellationToken))!;
    }

    private async Task<List<AuditLog>> AuditFor(Guid targetId, AuditAction action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditLogs.AsNoTracking()
            .Where(log => log.TargetId == targetId && log.Action == action)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountAuditAsync(AuditAction action, Guid actorUserId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditLogs.CountAsync(
            log => log.Action == action && log.ActorUserId == actorUserId,
            TestContext.Current.CancellationToken);
    }

    private async Task<List<AuditLog>> RecentAuditAsync(
        AuditAction action,
        DateTimeOffset since,
        Func<AuditLog, bool> predicate)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.AuditLogs.AsNoTracking()
            .Where(log => log.Action == action && log.OccurredAtUtc >= since)
            .ToListAsync(TestContext.Current.CancellationToken);

        return rows.Where(predicate).ToList();
    }

    private async Task<List<AuditLog>> AllAuditRowsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditLogs.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> DeclareCashAsync(HttpClient client, Guid reservationId) =>
        client.PostAsync(
            $"/api/reservations/{reservationId}/payments/cash", null, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> ConfirmCashAsync(HttpClient client, Guid paymentId) =>
        client.PostAsync(
            $"/api/payments/{paymentId}/cash/confirm", null, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> InitiateAsync(HttpClient client, Guid reservationId) =>
        client.PostAsync(
            $"/api/reservations/{reservationId}/payments/mercadopago",
            null,
            TestContext.Current.CancellationToken);

    private static async Task<Guid> ReadPaymentIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdResponse>(
            JsonOptions, TestContext.Current.CancellationToken))!.PaymentId;
    }

    private async Task<Payment> StartMercadoPagoAsync(HttpClient client, Guid reservationId)
    {
        var paymentId = await ReadPaymentIdAsync(await InitiateAsync(client, reservationId));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Payments.AsNoTracking().SingleAsync(
            payment => payment.Id == paymentId, TestContext.Current.CancellationToken);
    }

    private async Task ApproveAsync(Payment payment)
    {
        _fake.SetStatus(payment.ProviderOrderId!, "processed", "accredited");
        var response = await PostWebhookAsync(payment.ProviderOrderId!);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string orderId, string? requestId = null)
    {
        requestId ??= Guid.NewGuid().ToString();
        var signature = WebhookSigner.Header(
            WebhookSecret,
            orderId,
            requestId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/webhooks/mercadopago?data.id={Uri.EscapeDataString(orderId)}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    action = "order.processed",
                    type = "order",
                    data = new { id = orderId }
                }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("x-signature", signature);
        request.Headers.Add("x-request-id", requestId);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<Guid> CreateReservationAsync(HttpClient client, DateOnly date)
    {
        var start = new DateTimeOffset(
            date.ToDateTime(new TimeOnly(10, 0)), BuildingOffset).ToUniversalTime();

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

        return (await response.Content.ReadFromJsonAsync<CreatedReservation>(
            JsonOptions, TestContext.Current.CancellationToken))!.Id;
    }

    private async Task SetHoldDeadlineAsync(Guid reservationId, DateTimeOffset deadline)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Reservations\" SET \"ExpiresAtUtc\" = {deadline.ToUniversalTime()} WHERE \"Id\" = {reservationId}",
            TestContext.Current.CancellationToken);
    }

    private async Task ExpireAsync(DateTimeOffset now)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await TestServices.Expiration(db, new ManualTimeProvider(now))
            .ExpirePastHoldsAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<Guid>> InsertPendingReservationsAsync(int count)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var created = RaceDeadline.AddDays(-1);
        var reservations = Enumerable.Range(0, count)
            .Select(_ => new Reservation(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ReservationUseType.SharedLeisure,
                RaceDeadline.AddDays(1),
                RaceDeadline.AddDays(1).AddHours(1),
                created,
                RaceDeadline))
            .ToList();

        db.Reservations.AddRange(reservations);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return reservations.Select(reservation => reservation.Id).ToList();
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
}
