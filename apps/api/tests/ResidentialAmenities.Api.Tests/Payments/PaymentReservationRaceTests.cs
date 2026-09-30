using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Application;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Payments;

/// <summary>
/// The critical race in issue #24: a trusted payment confirmation
/// (<c>Pending → Confirmed</c>) versus the expiration job
/// (<c>Pending → Expired</c>) on the very same reservation rows, run truly
/// concurrently against real PostgreSQL with separate DbContexts.
///
/// Fixtures use a deadline far in the past, and clocks that straddle it, so
/// the global expiration UPDATE can only ever match these rows and never
/// touches reservations owned by other tests. Two clocks are used to model
/// two servers that disagree about "now" by a couple of seconds — the case
/// that makes both operations legitimately eligible at the same moment.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class PaymentReservationRaceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Deadline =
        new(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);

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
    public async Task ConcurrentConfirmationAndExpiration_NeverProduceBothOutcomes()
    {
        // Keep this comfortably below the CI Postgres max_connections limit:
        // the test starts two real DbContext operations per reservation.
        const int count = 20;
        var ids = await InsertPendingReservationsAsync(count);

        var confirmationClock = new ManualTimeProvider(Deadline.AddSeconds(-1));
        var expirationClock = new ManualTimeProvider(Deadline.AddSeconds(1));

        var tasks = new List<Task<ReservationConfirmationOutcome?>>();

        foreach (var id in ids)
        {
            tasks.Add(Task.Run<ReservationConfirmationOutcome?>(async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var contract = TestServices.Contract(
                    scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                    confirmationClock);

                return await contract.ConfirmPaidReservationAsync(
                    id, TestContext.Current.CancellationToken);
            }));

            tasks.Add(Task.Run<ReservationConfirmationOutcome?>(async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var expiration = TestServices.Expiration(
                    scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                    expirationClock);

                await expiration.ExpirePastHoldsAsync(TestContext.Current.CancellationToken);
                return null;
            }));
        }

        var results = await Task.WhenAll(tasks);
        var confirmationResults = results.Where(result => result is not null).ToList();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await dbContext.Reservations
            .AsNoTracking()
            .Where(reservation => ids.Contains(reservation.Id))
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(count, stored.Count);

        foreach (var reservation in stored)
        {
            // Exactly one terminal outcome, with self-consistent timestamps.
            switch (reservation.Status)
            {
                case ReservationStatus.Confirmed:
                    Assert.NotNull(reservation.ConfirmedAtUtc);
                    Assert.Null(reservation.ExpiredAtUtc);
                    break;
                case ReservationStatus.Expired:
                    Assert.NotNull(reservation.ExpiredAtUtc);
                    Assert.Null(reservation.ConfirmedAtUtc);
                    break;
                default:
                    Assert.Fail($"Unexpected final status {reservation.Status}.");
                    break;
            }
        }

        // What the confirmation side was told matches what was persisted.
        var confirmedInDb = stored.Count(r => r.Status == ReservationStatus.Confirmed);
        var confirmedAccordingToCallers = confirmationResults.Count(
            result => result == ReservationConfirmationOutcome.Confirmed);

        Assert.Equal(confirmedInDb, confirmedAccordingToCallers);
        Assert.All(
            confirmationResults.Where(result => result != ReservationConfirmationOutcome.Confirmed),
            result => Assert.Equal(ReservationConfirmationOutcome.RejectedExpired, result));
    }

    [Fact]
    public async Task ConfirmationInFlight_WhenExpirationCommitsFirst_SeesExpiredAndDoesNotRevive()
    {
        // Deterministic interleaving: a test transaction holds the row lock
        // (standing in for an expiration UPDATE that is mid-flight). A
        // confirmation starts and must WAIT for that lock rather than act on
        // a stale "Pending" read. When expiration then commits, the
        // confirmation must see Expired. Without the row lock in
        // ConfirmPaidReservationAsync the confirmation would read Pending
        // immediately and then overwrite Expired with Confirmed.
        var id = (await InsertPendingReservationsAsync(1)).Single();

        await using var holderScope = _factory.Services.CreateAsyncScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var holderTransaction =
            await holder.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Reservations\" WHERE \"Id\" = {id} FOR UPDATE",
            TestContext.Current.CancellationToken);

        var confirmation = Task.Run(async () =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var contract = TestServices.Contract(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                new ManualTimeProvider(Deadline.AddSeconds(-1)));

            return await contract.ConfirmPaidReservationAsync(
                id, TestContext.Current.CancellationToken);
        });

        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.False(confirmation.IsCompleted, "confirmation must be waiting on the row lock");

        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Reservations\" SET \"Status\" = 'Expired', \"ExpiredAtUtc\" = {Deadline.AddSeconds(1)} WHERE \"Id\" = {id}",
            TestContext.Current.CancellationToken);
        await holderTransaction.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReservationConfirmationOutcome.RejectedExpired, await confirmation);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var stored = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Reservations.AsNoTracking()
            .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);

        Assert.Equal(ReservationStatus.Expired, stored.Status);
        Assert.Null(stored.ConfirmedAtUtc);
    }

    [Fact]
    public async Task ExpirationInFlight_WhenConfirmationCommitsFirst_LeavesConfirmedUntouched()
    {
        var id = (await InsertPendingReservationsAsync(1)).Single();

        await using var holderScope = _factory.Services.CreateAsyncScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var holderTransaction =
            await holder.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Reservations\" WHERE \"Id\" = {id} FOR UPDATE",
            TestContext.Current.CancellationToken);

        var expiration = Task.Run(async () =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            return await TestServices.Expiration(
                    scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                    new ManualTimeProvider(Deadline.AddSeconds(1)))
                .ExpirePastHoldsAsync(TestContext.Current.CancellationToken);
        });

        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.False(expiration.IsCompleted, "expiration must be waiting on the row lock");

        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Reservations\" SET \"Status\" = 'Confirmed', \"ConfirmedAtUtc\" = {Deadline.AddSeconds(-1)} WHERE \"Id\" = {id}",
            TestContext.Current.CancellationToken);
        await holderTransaction.CommitAsync(TestContext.Current.CancellationToken);

        // Its WHERE Status = 'Pending' is re-evaluated after the lock frees:
        // the now-Confirmed row no longer matches.
        Assert.Equal(0, await expiration);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var stored = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Reservations.AsNoTracking()
            .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);

        Assert.Equal(ReservationStatus.Confirmed, stored.Status);
        Assert.Null(stored.ExpiredAtUtc);
    }

    [Fact]
    public async Task ConfirmationFirst_ThenExpirationJob_LeavesConfirmedUntouched()
    {
        var id = (await InsertPendingReservationsAsync(1)).Single();

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var contract = TestServices.Contract(
            dbContext, new ManualTimeProvider(Deadline.AddSeconds(-1)));
        Assert.Equal(
            ReservationConfirmationOutcome.Confirmed,
            await contract.ConfirmPaidReservationAsync(id, TestContext.Current.CancellationToken));

        var expiredCount = await TestServices.Expiration(
            dbContext, new ManualTimeProvider(Deadline.AddHours(1)))
            .ExpirePastHoldsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, expiredCount);

        var stored = await dbContext.Reservations.AsNoTracking()
            .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
        Assert.Equal(ReservationStatus.Confirmed, stored.Status);
    }

    [Fact]
    public async Task ExpirationFirst_ThenConfirmation_DoesNotRevive()
    {
        var id = (await InsertPendingReservationsAsync(1)).Single();

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await TestServices.Expiration(
            dbContext, new ManualTimeProvider(Deadline.AddSeconds(1)))
            .ExpirePastHoldsAsync(TestContext.Current.CancellationToken);

        // Even with a clock that (wrongly) thinks it is before the deadline,
        // the persisted Expired state wins: no revival.
        var outcome = await TestServices.Contract(
            dbContext, new ManualTimeProvider(Deadline.AddSeconds(-1)))
            .ConfirmPaidReservationAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(ReservationConfirmationOutcome.RejectedExpired, outcome);

        var stored = await dbContext.Reservations.AsNoTracking()
            .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
        Assert.Equal(ReservationStatus.Expired, stored.Status);
        Assert.Null(stored.ConfirmedAtUtc);
    }

    [Fact]
    public async Task ConfirmingAnUnknownReservation_ReportsNotFound()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var contract = TestServices.Contract(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            new ManualTimeProvider(Deadline));

        Assert.Equal(
            ReservationConfirmationOutcome.NotFound,
            await contract.ConfirmPaidReservationAsync(
                Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    private async Task<List<Guid>> InsertPendingReservationsAsync(int count)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var created = Deadline.AddDays(-1);
        var reservations = Enumerable.Range(0, count)
            .Select(_ => new Reservation(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ReservationUseType.SharedLeisure,
                Deadline.AddDays(1),
                Deadline.AddDays(1).AddHours(1),
                created,
                Deadline))
            .ToList();

        dbContext.Reservations.AddRange(reservations);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return reservations.Select(reservation => reservation.Id).ToList();
    }
}
