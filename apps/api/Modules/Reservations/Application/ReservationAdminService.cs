using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Infrastructure.Persistence;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Administrative reservation commands (RF-018, RB-013, RB-014). Every command
/// runs in one transaction that takes the reservation row lock (the same lock
/// payment confirmation and the expiration UPDATE contend on), applies the
/// change through the aggregate, and records its audit entry in the same
/// SaveChanges: the change and its audit fact are committed together or not
/// at all. Nothing here touches payments, pricing snapshots or the hold.
/// </summary>
public sealed class ReservationAdminService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IAuditRecorder auditRecorder,
    ReservationScheduleValidator scheduleValidator) : IReservationAdminContract
{
    public async Task<ReservationCancelOutcome> CancelAsync(
        Guid reservationId,
        string reason,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var normalizedReason = NormalizeReason(reason);
        var nowUtc = timeProvider.GetUtcNow();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await LockReservationAsync(reservationId, cancellationToken);

        var reservation = await dbContext.Reservations.SingleOrDefaultAsync(
            candidate => candidate.Id == reservationId,
            cancellationToken);

        if (reservation is null)
        {
            return ReservationCancelOutcome.NotFound;
        }

        if (reservation.Status == ReservationStatus.Cancelled)
        {
            return ReservationCancelOutcome.AlreadyCancelled;
        }

        // DEC-014/RB-021 (OQ-011): an Event reservation may only be
        // cancelled 24 hours or more before its start; exactly 24 hours
        // before is the last allowed instant (>= 24h allowed, < 24h
        // rejected). Free Leisure (RB-018) has no financial cancellation
        // policy and is unaffected by this window. This is the
        // cancellation window only — no refund/financial-consequence logic
        // is implemented here (RB-016 remains open).
        if (reservation.UseType == ReservationUseType.Event &&
            reservation.StartsAtUtc - nowUtc < TimeSpan.FromHours(24))
        {
            return ReservationCancelOutcome.TooCloseToStart;
        }

        if (!reservation.Cancel(nowUtc, normalizedReason))
        {
            return ReservationCancelOutcome.NotCancellable;
        }

        auditRecorder.Record(AuditRecord.ByUser(
            actorUserId,
            AuditAction.ReservationCancelled,
            AuditTargetType.Reservation,
            reservation.Id,
            reservation.BuildingId,
            AuditMetadata.ReservationCancelled(normalizedReason)));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ReservationCancelOutcome.Cancelled;
    }

    public async Task<ReservationRescheduleResult> RescheduleAsync(
        Guid reservationId,
        DateTimeOffset newStartsAtUtc,
        DateTimeOffset newEndsAtUtc,
        string reason,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var normalizedReason = NormalizeReason(reason);

        // Npgsql only persists zero-offset timestamps.
        newStartsAtUtc = UtcInstant.Normalize(newStartsAtUtc);
        newEndsAtUtc = UtcInstant.Normalize(newEndsAtUtc);

        if (newEndsAtUtc <= newStartsAtUtc)
        {
            throw new ReservationRequestException(
                "End must be after start.",
                StatusCodes.Status400BadRequest);
        }

        // Read only to learn which resources to lock; every decision below is
        // re-made on fresh state after the locks are held.
        var resourceAmenityIds = await dbContext.ReservationResources
            .AsNoTracking()
            .Where(resource => resource.ReservationId == reservationId)
            .Select(resource => resource.AmenityId)
            .ToListAsync(cancellationToken);

        if (resourceAmenityIds.Count == 0 &&
            !await dbContext.Reservations.AnyAsync(
                candidate => candidate.Id == reservationId,
                cancellationToken))
        {
            throw new ReservationRequestException(
                "Reservation not found.",
                StatusCodes.Status404NotFound);
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Fixed order: the reservation row first, then the per-Amenity
        // advisory locks (creation only takes the latter, confirmation and
        // cancellation only the former), so no cycle can form.
        await LockReservationAsync(reservationId, cancellationToken);
        await ResourceAdvisoryLock.AcquireAsync(dbContext, resourceAmenityIds, cancellationToken);

        var reservation = await dbContext.Reservations
            .Include(candidate => candidate.Resources)
            .SingleOrDefaultAsync(candidate => candidate.Id == reservationId, cancellationToken)
            ?? throw new ReservationRequestException(
                "Reservation not found.",
                StatusCodes.Status404NotFound);

        var nowUtc = timeProvider.GetUtcNow();

        var reschedulable =
            reservation.Status == ReservationStatus.Confirmed ||
            (reservation.Status == ReservationStatus.Pending && nowUtc < reservation.ExpiresAtUtc);

        if (!reschedulable)
        {
            throw new ReservationRequestException(
                $"A {reservation.Status} reservation cannot be rescheduled; only a " +
                "Confirmed reservation or a Pending one whose hold is still active can.",
                StatusCodes.Status409Conflict);
        }

        if (reservation.StartsAtUtc == newStartsAtUtc && reservation.EndsAtUtc == newEndsAtUtc)
        {
            return new ReservationRescheduleResult(Changed: false);
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == reservation.BuildingId, cancellationToken);

        var amenityIds = reservation.Resources.Select(resource => resource.AmenityId).ToList();

        var amenities = await dbContext.Amenities
            .AsNoTracking()
            .Include(candidate => candidate.AvailabilityWindows)
            .Include(candidate => candidate.UnavailablePeriods)
            .Where(candidate => amenityIds.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);

        // The same invariants as creation, through the shared validator.
        foreach (var amenity in amenities.Values)
        {
            scheduleValidator.EnsureWithinAvailability(
                amenity,
                building.TimeZoneId,
                newStartsAtUtc,
                newEndsAtUtc);
        }

        if (reservation.UseType == ReservationUseType.Event)
        {
            await scheduleValidator.EnsureValidEventSlotAsync(
                reservation.BuildingId,
                building.TimeZoneId,
                newStartsAtUtc,
                newEndsAtUtc,
                cancellationToken);
        }

        await scheduleValidator.EnsureNoConflictAsync(
            reservation.BuildingId,
            reservation.Resources
                .Select(resource => new ScheduledResource(
                    resource.AmenityId,
                    amenities.TryGetValue(resource.AmenityId, out var known)
                        ? known.Name
                        : resource.AmenityId.ToString(),
                    resource.IsExclusive))
                .ToList(),
            newStartsAtUtc,
            newEndsAtUtc,
            nowUtc,
            excludeReservationId: reservation.Id,
            cancellationToken);

        var previousStartsAtUtc = reservation.StartsAtUtc;
        var previousEndsAtUtc = reservation.EndsAtUtc;

        if (!reservation.TryReschedule(newStartsAtUtc, newEndsAtUtc, nowUtc))
        {
            throw new ReservationRequestException(
                "The reservation can no longer be rescheduled.",
                StatusCodes.Status409Conflict);
        }

        auditRecorder.Record(AuditRecord.ByUser(
            actorUserId,
            AuditAction.ReservationRescheduled,
            AuditTargetType.Reservation,
            reservation.Id,
            reservation.BuildingId,
            AuditMetadata.ReservationRescheduled(
                previousStartsAtUtc,
                previousEndsAtUtc,
                newStartsAtUtc,
                newEndsAtUtc,
                normalizedReason)));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ReservationRescheduleResult(Changed: true);
    }

    private Task<int> LockReservationAsync(Guid reservationId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Reservations\" WHERE \"Id\" = {reservationId} FOR UPDATE",
            cancellationToken);

    /// <summary>The reason is mandatory (RB-014): trimmed, not empty, at most 500 characters.</summary>
    private static string NormalizeReason(string? reason)
    {
        var trimmed = reason?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ReservationRequestException(
                "A reason is required.",
                StatusCodes.Status400BadRequest);
        }

        if (trimmed.Length > AuditMetadata.MaxReasonLength)
        {
            throw new ReservationRequestException(
                $"The reason cannot exceed {AuditMetadata.MaxReasonLength} characters.",
                StatusCodes.Status400BadRequest);
        }

        return trimmed;
    }
}
