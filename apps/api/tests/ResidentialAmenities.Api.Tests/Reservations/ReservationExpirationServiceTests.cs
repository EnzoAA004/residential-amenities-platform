using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Application;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Reservations;

/// <summary>
/// Exercises ReservationExpirationService directly (no HTTP) against a real
/// PostgreSQL database, with a manual clock instead of sleeping, per issue
/// #23's TDD/determinism requirements.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class ReservationExpirationServiceTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("environment", "Development"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ExpirePastHolds_TransitionsOnlyPendingPastDeadline()
    {
        // Anchored to real current time (not a fixed future date): other
        // tests sharing this database create real Pending holds that expire
        // ~30 minutes from their own creation, and this run must not treat
        // those as past-due too. Truncated to microseconds because
        // PostgreSQL's timestamptz has microsecond resolution while
        // DateTimeOffset has 100ns ticks — an untruncated value would never
        // exactly round-trip.
        var now = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var clock = new ManualTimeProvider(now);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pastDue = CreateReservation(
            ReservationStatus.Pending,
            createdAtUtc: now.AddHours(-2),
            expiresAtUtc: now.AddMinutes(-1));

        var notYetDue = CreateReservation(
            ReservationStatus.Pending,
            createdAtUtc: now.AddMinutes(-10),
            expiresAtUtc: now.AddMinutes(10));

        // A reservation that a future Payments confirmation (#24/#25) has
        // already moved to Confirmed. There is no public API to create one
        // directly yet (RB-009/RB-010 only define the hold and its
        // expiration, not confirmation), so this fixture flips it with a
        // raw statement after insert purely to prove the expiration query's
        // WHERE clause leaves Confirmed rows alone even when their
        // (otherwise unused) ExpiresAtUtc has passed.
        var alreadyConfirmed = CreateReservation(
            ReservationStatus.Pending,
            createdAtUtc: now.AddHours(-5),
            expiresAtUtc: now.AddMinutes(-1));

        dbContext.Reservations.AddRange(pastDue, notYetDue, alreadyConfirmed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Reservations\" SET \"Status\" = 'Confirmed' WHERE \"Id\" = {alreadyConfirmed.Id}",
            TestContext.Current.CancellationToken);

        var service = new ReservationExpirationService(dbContext, clock);
        var expiredCount = await service.ExpirePastHoldsAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(1, expiredCount);

        await AssertStatusAsync(dbContext, pastDue.Id, ReservationStatus.Expired, now);
        await AssertStatusAsync(dbContext, notYetDue.Id, ReservationStatus.Pending, null);
        await AssertStatusAsync(
            dbContext,
            alreadyConfirmed.Id,
            ReservationStatus.Confirmed,
            null);
    }

    [Fact]
    public async Task ExpirePastHolds_CalledTwice_IsIdempotent()
    {
        var now = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var clock = new ManualTimeProvider(now);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pastDue = CreateReservation(
            ReservationStatus.Pending,
            createdAtUtc: now.AddHours(-2),
            expiresAtUtc: now.AddMinutes(-1));

        dbContext.Reservations.Add(pastDue);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = new ReservationExpirationService(dbContext, clock);

        var firstRun = await service.ExpirePastHoldsAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(1, firstRun);

        clock.Advance(TimeSpan.FromMinutes(5));

        var secondRun = await service.ExpirePastHoldsAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(0, secondRun);

        // The already-expired row must keep its original ExpiredAtUtc — the
        // second run must not "re-expire" it with a later timestamp.
        await AssertStatusAsync(dbContext, pastDue.Id, ReservationStatus.Expired, now);
    }

    [Fact]
    public async Task ExpirePastHolds_TwoConcurrentRuns_BothSucceedExactlyOnce()
    {
        var now = DateTimeOffset.UtcNow;

        await using var setupScope = _factory.Services.CreateAsyncScope();
        var setupDbContext =
            setupScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reservations = Enumerable.Range(0, 20)
            .Select(_ => CreateReservation(
                ReservationStatus.Pending,
                createdAtUtc: now.AddHours(-2),
                expiresAtUtc: now.AddMinutes(-1)))
            .ToList();

        setupDbContext.Reservations.AddRange(reservations);
        await setupDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Two independent scopes/DbContext instances, each racing to expire
        // the same batch of past-due holds — simulating two worker
        // processes. PostgreSQL's row-level locking under the bulk
        // UPDATE ... WHERE makes this safe without any lock of our own.
        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();

        var serviceA = new ReservationExpirationService(
            scopeA.ServiceProvider.GetRequiredService<AppDbContext>(),
            new ManualTimeProvider(now));
        var serviceB = new ReservationExpirationService(
            scopeB.ServiceProvider.GetRequiredService<AppDbContext>(),
            new ManualTimeProvider(now));

        var cancellationToken = TestContext.Current.CancellationToken;
        var results = await Task.WhenAll(
            serviceA.ExpirePastHoldsAsync(cancellationToken),
            serviceB.ExpirePastHoldsAsync(cancellationToken));

        // Every row is expired exactly once in total, split however the two
        // runs happened to race — never double-counted, never missed.
        Assert.Equal(20, results.Sum());

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDbContext =
            verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expiredCount = await verifyDbContext.Reservations
            .Where(reservation =>
                reservations.Select(r => r.Id).Contains(reservation.Id) &&
                reservation.Status == ReservationStatus.Expired)
            .CountAsync(cancellationToken);

        Assert.Equal(20, expiredCount);
    }

    /// <summary>
    /// PostgreSQL's timestamptz stores microsecond precision; DateTimeOffset
    /// ticks are 100ns. Without this, an exact-equality assertion against a
    /// value that round-tripped through Postgres can fail by a fraction of a
    /// microsecond.
    /// </summary>
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % 10), value.Offset);

    private static async Task AssertStatusAsync(
        AppDbContext dbContext,
        Guid reservationId,
        ReservationStatus expectedStatus,
        DateTimeOffset? expectedExpiredAtUtc)
    {
        var reservation = await dbContext.Reservations
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == reservationId,
                TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, reservation.Status);
        Assert.Equal(expectedExpiredAtUtc, reservation.ExpiredAtUtc);
    }

    /// <summary>
    /// Every <see cref="Reservation"/> is created Pending; the
    /// <paramref name="status"/> parameter only documents the caller's
    /// intent at the call site (e.g. "this one will be flipped to Confirmed
    /// via raw SQL right after").
    /// </summary>
    private static Reservation CreateReservation(
        ReservationStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        _ = status;

        return new Reservation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReservationUseType.SharedLeisure,
            createdAtUtc,
            createdAtUtc.AddHours(1),
            createdAtUtc,
            expiresAtUtc);
    }
}
