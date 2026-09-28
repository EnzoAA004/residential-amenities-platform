using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Implements <see cref="IReservationPaymentContract"/>.
///
/// Payment-vs-expiration race: <c>ReservationExpirationService</c> flips
/// <c>Pending → Expired</c> with a single atomic UPDATE whose WHERE clause
/// requires <c>Status = 'Pending'</c>. Confirmation takes a row-level
/// <c>SELECT ... FOR UPDATE</c> on the same row inside a transaction, then
/// applies <see cref="Reservation.Confirm"/> on the freshly-read state. Both
/// paths lock the same row, so PostgreSQL serializes them: whichever commits
/// first wins, and the other then observes the committed state — the
/// expiration UPDATE re-evaluates its WHERE and skips a now-Confirmed row;
/// the confirmation, blocked on the lock, reads <c>Expired</c> and
/// <see cref="Reservation.Confirm"/> refuses to revive it. A reservation can
/// therefore never end up both Confirmed and Expired.
/// </summary>
public sealed class ReservationPaymentContract(
    AppDbContext dbContext,
    TimeProvider timeProvider) : IReservationPaymentContract
{
    public async Task<PayableReservation?> GetPayableReservationAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations
            .AsNoTracking()
            .Include(candidate => candidate.PriceLines)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == reservationId,
                cancellationToken);

        if (reservation is null)
        {
            return null;
        }

        var currencies = reservation.PriceLines
            .Select(line => line.Currency)
            .Distinct()
            .ToList();

        return new PayableReservation(
            reservation.Id,
            reservation.BuildingId,
            reservation.Status,
            reservation.ExpiresAtUtc,
            reservation.PriceLines.Sum(line => line.Amount),
            currencies.Count == 1 ? currencies[0] : null,
            currencies.Count == 1,
            reservation.PriceLines.Count > 0);
    }

    public async Task<ReservationConfirmationOutcome> ConfirmPaidReservationAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // FromSql without further composition, so EF does not wrap the
        // FOR UPDATE in a subquery.
        var locked = await dbContext.Reservations
            .FromSqlInterpolated(
                $"SELECT * FROM \"Reservations\" WHERE \"Id\" = {reservationId} FOR UPDATE")
            .ToListAsync(cancellationToken);

        var reservation = locked.SingleOrDefault();

        if (reservation is null)
        {
            return ReservationConfirmationOutcome.NotFound;
        }

        var wasConfirmed = reservation.Status == ReservationStatus.Confirmed;
        var confirmed = reservation.Confirm(timeProvider.GetUtcNow());

        if (confirmed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return wasConfirmed
                ? ReservationConfirmationOutcome.AlreadyConfirmed
                : ReservationConfirmationOutcome.Confirmed;
        }

        return reservation.Status == ReservationStatus.Cancelled
            ? ReservationConfirmationOutcome.RejectedCancelled
            : ReservationConfirmationOutcome.RejectedExpired;
    }
}
