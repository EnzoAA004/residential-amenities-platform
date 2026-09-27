using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Releases expired payment holds (RF-012, RB-010): flips
/// <see cref="ReservationStatus.Pending"/> reservations whose
/// <see cref="Reservation.ExpiresAtUtc"/> has passed to
/// <see cref="ReservationStatus.Expired"/>.
///
/// Implemented as a single atomic <c>UPDATE ... WHERE</c> via EF Core's
/// <c>ExecuteUpdateAsync</c> rather than loading entities into the change
/// tracker. This is what makes it safe under real concurrent execution
/// without any extra locking: PostgreSQL evaluates and locks each candidate
/// row individually, and under READ COMMITTED (the default), a statement
/// blocked on a row another transaction is updating re-checks that row's
/// <c>WHERE</c> predicate against the just-committed value once unblocked.
/// So if two expiration runs overlap, whichever commits first flips a row
/// to <c>Expired</c>; the second run's predicate (<c>Status = 'Pending'</c>)
/// then no longer matches that row, and it is simply left alone — no
/// exception, no double-processing, no lock of our own required. Calling
/// this twice in a row is equally idempotent: the second call's `WHERE`
/// matches nothing new and it returns 0.
///
/// This never touches <see cref="ReservationStatus.Confirmed"/> or
/// <see cref="ReservationStatus.Cancelled"/> reservations, and never
/// "revives" an already-<see cref="ReservationStatus.Expired"/> one, because
/// the `WHERE` clause only ever matches rows still in `Pending`.
/// </summary>
public sealed class ReservationExpirationService(
    AppDbContext dbContext,
    TimeProvider timeProvider)
{
    public Task<int> ExpirePastHoldsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();

        return dbContext.Reservations
            .Where(reservation =>
                reservation.Status == ReservationStatus.Pending &&
                reservation.ExpiresAtUtc <= nowUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        reservation => reservation.Status,
                        ReservationStatus.Expired)
                    .SetProperty(
                        reservation => reservation.ExpiredAtUtc,
                        nowUtc),
                cancellationToken);
    }
}
