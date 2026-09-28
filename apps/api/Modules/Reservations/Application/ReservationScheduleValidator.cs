using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Amenities.Application;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

public sealed record ScheduledResource(Guid AmenityId, string AmenityName, bool IsExclusive);

/// <summary>
/// The scheduling invariants a reservation must satisfy for a given time
/// range, shared by reservation creation and administrative reschedule so the
/// rules are never duplicated: availability windows and maintenance periods,
/// Event slot match, and conflict detection (shared/exclusive compatibility,
/// active holds only). Callers are responsible for holding the per-Amenity
/// advisory locks before calling <see cref="EnsureNoConflictAsync"/>.
/// </summary>
public sealed class ReservationScheduleValidator(AppDbContext dbContext)
{
    public void EnsureWithinAvailability(
        Amenity amenity,
        string timeZoneId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc)
    {
        IReadOnlyList<AvailabilityInterval> openIntervals;

        try
        {
            openIntervals = AmenityAvailabilityCalculator.CalculateOpenIntervals(
                amenity.AvailabilityWindows,
                amenity.UnavailablePeriods,
                timeZoneId,
                startsAtUtc,
                endsAtUtc);
        }
        catch (ArgumentException error)
        {
            throw new ReservationRequestException(
                error.Message,
                StatusCodes.Status400BadRequest);
        }

        var requestedDuration = endsAtUtc - startsAtUtc;
        var coveredDuration = openIntervals.Aggregate(
            TimeSpan.Zero,
            (total, interval) => total + (interval.EndUtc - interval.StartUtc));

        if (coveredDuration < requestedDuration)
        {
            throw new ReservationRequestException(
                $"'{amenity.Name}' is outside its configured " +
                "availability or falls within a maintenance/unavailable " +
                "period for the requested range.",
                StatusCodes.Status422UnprocessableEntity);
        }
    }

    public async Task EnsureValidEventSlotAsync(
        Guid buildingId,
        string timeZoneId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        CancellationToken cancellationToken)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        var localStart = TimeZoneInfo.ConvertTime(startsAtUtc, timeZone);
        var localEnd = TimeZoneInfo.ConvertTime(endsAtUtc, timeZone);

        if (localStart.Date != localEnd.Date)
        {
            throw new ReservationRequestException(
                "Event reservations may not cross midnight in the " +
                "building's local time zone yet.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var startTime = TimeOnly.FromDateTime(localStart.DateTime);
        var endTime = TimeOnly.FromDateTime(localEnd.DateTime);

        var slots = await dbContext.EventSlotDefinitions
            .AsNoTracking()
            .Where(slot => slot.BuildingId == buildingId)
            .ToListAsync(cancellationToken);

        if (!slots.Any(slot => slot.Matches(startTime, endTime)))
        {
            throw new ReservationRequestException(
                "The requested range does not match a configured Event " +
                "slot for this building. Exact Event slot times remain " +
                "configurable/TBD pending issue #2.",
                StatusCodes.Status422UnprocessableEntity);
        }
    }

    /// <param name="excludeReservationId">
    /// A reservation being moved must not conflict with itself.
    /// </param>
    public async Task EnsureNoConflictAsync(
        Guid buildingId,
        IReadOnlyList<ScheduledResource> resources,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        DateTimeOffset nowUtc,
        Guid? excludeReservationId,
        CancellationToken cancellationToken)
    {
        var amenityIds = resources.Select(resource => resource.AmenityId).ToList();

        // Only a cheap prefilter runs in SQL (resource ids + building +
        // status/expiry); the actual compatibility decision always goes
        // through the single centralized ReservationCompatibility
        // .ConflictsWith rule so it is never duplicated between endpoints or
        // resources. A Pending hold blocks exactly like Confirmed while it
        // has not yet passed its own ExpiresAtUtc (RB-009/RB-010) — checked
        // here at query time so a hold stops blocking the instant it is
        // past due, even if the expiration job has not run yet.
        var candidates = await dbContext.ReservationResources
            .AsNoTracking()
            .Where(resource =>
                amenityIds.Contains(resource.AmenityId) &&
                resource.Reservation.BuildingId == buildingId &&
                (excludeReservationId == null ||
                 resource.ReservationId != excludeReservationId) &&
                (resource.Reservation.Status == ReservationStatus.Confirmed ||
                 (resource.Reservation.Status == ReservationStatus.Pending &&
                  resource.Reservation.ExpiresAtUtc > nowUtc)))
            .Select(resource => new
            {
                resource.AmenityId,
                resource.IsExclusive,
                resource.Reservation.StartsAtUtc,
                resource.Reservation.EndsAtUtc
            })
            .ToListAsync(cancellationToken);

        foreach (var resource in resources)
        {
            var hasConflict = candidates
                .Where(candidate => candidate.AmenityId == resource.AmenityId)
                .Any(candidate => ReservationCompatibility.ConflictsWith(
                    candidate.IsExclusive,
                    resource.IsExclusive,
                    candidate.StartsAtUtc,
                    candidate.EndsAtUtc,
                    startsAtUtc,
                    endsAtUtc));

            if (hasConflict)
            {
                throw new ReservationConflictException(
                    $"The requested time range conflicts with an existing " +
                    $"incompatible reservation for '{resource.AmenityName}'.");
            }
        }
    }
}
