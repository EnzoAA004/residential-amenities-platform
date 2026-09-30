using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Application;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations;

/// <summary>
/// Resident-facing discovery of a building's <b>configured</b> Event slots
/// (issue #62) — distinct from <c>/api/admin/buildings/{id}/event-slots</c>
/// (Administrator-only, exposes inactive slots and admin fields). This
/// endpoint intentionally returns a narrower, resident-safe shape and never
/// relaxes the admin endpoint's policy.
///
/// This is configuration, not availability: it reports which Event slots
/// exist and are active for the requested calendar date, expanded to UTC
/// instants using the building's own time zone. It never looks at
/// <c>Reservations</c>/<c>ReservationResources</c>/pricing/amenity
/// availability to decide what to return — a slot that is fully booked (or
/// even in maintenance) still appears here, exactly as
/// <c>AmenityAvailabilityCalculator</c> reports structural amenity
/// availability without knowing about reservations. <c>POST
/// /api/reservations</c> remains the sole authority for conflicts/business
/// validation when a resident actually tries to book one.
/// </summary>
public static class EventSlotEndpoints
{
    public static IEndpointRouteBuilder MapEventSlotEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/buildings/{buildingId:guid}/event-slots",
                ListEventSlotOccurrencesAsync)
            .WithTags("Reservations")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        return endpoints;
    }

    private static async Task<IResult> ListEventSlotOccurrencesAsync(
        Guid buildingId,
        DateOnly date,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken)
    {
        if (!await membershipAuthorizer.HasAccessAsync(
                principal,
                buildingId,
                cancellationToken))
        {
            return Results.Problem(
                title: "You do not have access to this building.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == buildingId,
                cancellationToken);

        if (building is null)
        {
            return Results.NotFound();
        }

        var slots = await dbContext.EventSlotDefinitions
            .AsNoTracking()
            .Where(slot => slot.BuildingId == buildingId && slot.IsActive)
            .OrderBy(slot => slot.StartTime)
            .ThenBy(slot => slot.EndTime)
            .ThenBy(slot => slot.Id)
            .ToListAsync(cancellationToken);

        if (slots.Count == 0)
        {
            return Results.Ok(Array.Empty<EventSlotOccurrenceResponse>());
        }

        // building.TimeZoneId is the sole authority for this conversion —
        // never the server's local time zone, a fixed offset, or a
        // hardcoded IANA id.
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(building.TimeZoneId);
        var occurrences = new List<EventSlotOccurrenceResponse>(slots.Count);

        foreach (var slot in slots)
        {
            DateTimeOffset startsAtUtc;
            DateTimeOffset endsAtUtc;

            try
            {
                startsAtUtc = ToUtc(date, slot.StartTime, timeZone);
                // An overnight slot's end time (e.g. 03:00) belongs to the
                // calendar day *after* the requested date, not the same
                // day — otherwise this would compute an end instant before
                // the start instant (DEC-014/OQ-002).
                endsAtUtc = ToUtc(
                    slot.IsOvernight ? date.AddDays(1) : date,
                    slot.EndTime,
                    timeZone);
            }
            catch (EventSlotTimeZoneException error)
            {
                // Conservative by design: never silently pick one of two
                // DST offsets, and never silently drop the affected slot —
                // both would produce a UTC instant the resident did not
                // actually choose. The caller must pick a different date.
                return Results.Problem(
                    title: "This Event slot cannot be represented " +
                           "unambiguously on this date in the building's " +
                           "time zone.",
                    detail: error.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            occurrences.Add(new EventSlotOccurrenceResponse(
                slot.Id,
                slot.Name,
                startsAtUtc,
                endsAtUtc,
                slot.IsOvernight));
        }

        return Results.Ok(occurrences);
    }

    /// <summary>
    /// Combines a building-local calendar date with a building-local
    /// <see cref="TimeOnly"/> and converts the result to UTC using
    /// <paramref name="timeZone"/>, rejecting a DST-invalid or DST-ambiguous
    /// local time explicitly instead of letting .NET resolve it silently.
    /// </summary>
    private static DateTimeOffset ToUtc(
        DateOnly date,
        TimeOnly time,
        TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(
            date.ToDateTime(time),
            DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(local))
        {
            throw new EventSlotTimeZoneException(
                $"{date:yyyy-MM-dd} {time} does not exist in time zone " +
                $"'{timeZone.Id}' (a spring-forward DST transition).");
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            throw new EventSlotTimeZoneException(
                $"{date:yyyy-MM-dd} {time} is ambiguous in time zone " +
                $"'{timeZone.Id}' (a fall-back DST transition).");
        }

        return new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(local, timeZone),
            TimeSpan.Zero);
    }

    private sealed class EventSlotTimeZoneException(string message)
        : Exception(message);

    private sealed record EventSlotOccurrenceResponse(
        Guid Id,
        string Name,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        bool IsOvernight);
}
