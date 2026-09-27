using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Amenities.Application;
using ResidentialAmenities.Api.Modules.Pricing.Application;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Orchestrates reservation creation across the modules Reservations
/// depends on (Amenities &amp; Availability, Pricing), per
/// docs/03-architecture/module-boundaries.md. It reuses
/// <see cref="AmenityAvailabilityCalculator"/> and
/// <see cref="PricingCalculator"/> rather than re-implementing either.
/// </summary>
public sealed class ReservationCreationService(AppDbContext dbContext)
{
    private static readonly HashSet<ReservationUseType> SupportedUseTypes =
    [
        ReservationUseType.SharedLeisure,
        ReservationUseType.ExclusiveLeisure
    ];

    public async Task<Reservation> CreateAsync(
        CreateReservationCommand command,
        CancellationToken cancellationToken)
    {
        // Npgsql only persists DateTimeOffset with a zero UTC offset into
        // `timestamptz` columns; normalize whatever offset the client sent
        // (any of them describe a valid, well-defined instant) up front.
        command = command with
        {
            StartsAtUtc = command.StartsAtUtc.ToUniversalTime(),
            EndsAtUtc = command.EndsAtUtc.ToUniversalTime()
        };

        if (command.EndsAtUtc <= command.StartsAtUtc)
        {
            throw new ReservationRequestException(
                "End must be after start.",
                StatusCodes.Status400BadRequest);
        }

        if (!SupportedUseTypes.Contains(command.UseType))
        {
            throw new ReservationRequestException(
                $"Reservation type '{command.UseType}' is not supported by " +
                "this endpoint yet (Event reservations are issue #21).",
                StatusCodes.Status400BadRequest);
        }

        var isExclusive = command.UseType == ReservationUseType.ExclusiveLeisure;

        var amenity = await dbContext.Amenities
            .Include(candidate => candidate.AvailabilityWindows)
            .Include(candidate => candidate.UnavailablePeriods)
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.AmenityId &&
                    candidate.BuildingId == command.BuildingId,
                cancellationToken);

        if (amenity is null)
        {
            throw new ReservationRequestException(
                "Amenity not found for this building.",
                StatusCodes.Status404NotFound);
        }

        if (!amenity.IsActive)
        {
            throw new ReservationRequestException(
                "This amenity is not currently active.",
                StatusCodes.Status422UnprocessableEntity);
        }

        if (isExclusive && !amenity.AllowsExclusiveUse)
        {
            throw new ReservationRequestException(
                "This amenity does not allow exclusive-leisure reservations.",
                StatusCodes.Status422UnprocessableEntity);
        }

        if (!isExclusive && !amenity.AllowsSharedUse)
        {
            throw new ReservationRequestException(
                "This amenity does not allow shared-leisure reservations.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == command.BuildingId,
                cancellationToken);

        EnsureWithinAvailability(amenity, building.TimeZoneId, command);

        await EnsureNoConflictAsync(command, isExclusive, cancellationToken);

        var rules = await dbContext.PriceRules
            .AsNoTracking()
            .Where(rule => rule.BuildingId == command.BuildingId)
            .ToListAsync(cancellationToken);

        var quotedAtUtc = DateTimeOffset.UtcNow;

        // PricingException (e.g. no active rule) propagates to the endpoint,
        // which already knows how to map it to a 422 ProblemDetails
        // response for the /api/pricing/quote endpoint.
        var quote = PricingCalculator.Calculate(
            rules,
            command.AmenityId,
            command.UseType,
            [],
            quotedAtUtc);

        var reservation = new Reservation(
            Guid.NewGuid(),
            command.BuildingId,
            command.MembershipId,
            command.UseType,
            command.StartsAtUtc,
            command.EndsAtUtc,
            quotedAtUtc);

        reservation.AddResource(Guid.NewGuid(), command.AmenityId, isExclusive);

        foreach (var line in quote.Lines)
        {
            reservation.AddPriceLine(
                Guid.NewGuid(),
                line.PriceRuleId,
                line.AmenityId,
                line.ComponentType,
                line.Currency,
                line.Amount,
                quotedAtUtc);
        }

        dbContext.Reservations.Add(reservation);
        await dbContext.SaveChangesAsync(cancellationToken);

        return reservation;
    }

    private static void EnsureWithinAvailability(
        Amenities.Domain.Amenity amenity,
        string timeZoneId,
        CreateReservationCommand command)
    {
        IReadOnlyList<AvailabilityInterval> openIntervals;

        try
        {
            openIntervals = AmenityAvailabilityCalculator.CalculateOpenIntervals(
                amenity.AvailabilityWindows,
                amenity.UnavailablePeriods,
                timeZoneId,
                command.StartsAtUtc,
                command.EndsAtUtc);
        }
        catch (ArgumentException error)
        {
            throw new ReservationRequestException(
                error.Message,
                StatusCodes.Status400BadRequest);
        }

        var requestedDuration = command.EndsAtUtc - command.StartsAtUtc;
        var coveredDuration = openIntervals.Aggregate(
            TimeSpan.Zero,
            (total, interval) => total + (interval.EndUtc - interval.StartUtc));

        if (coveredDuration < requestedDuration)
        {
            throw new ReservationRequestException(
                "The requested range is outside the amenity's configured " +
                "availability or falls within a maintenance/unavailable " +
                "period.",
                StatusCodes.Status422UnprocessableEntity);
        }
    }

    private async Task EnsureNoConflictAsync(
        CreateReservationCommand command,
        bool isExclusive,
        CancellationToken cancellationToken)
    {
        // Only a cheap prefilter runs in SQL (resource + building + status);
        // the actual compatibility decision always goes through the single
        // centralized ReservationCompatibility.ConflictsWith rule so it is
        // never duplicated between endpoints.
        var candidates = await dbContext.ReservationResources
            .AsNoTracking()
            .Where(resource =>
                resource.AmenityId == command.AmenityId &&
                resource.Reservation.BuildingId == command.BuildingId &&
                resource.Reservation.Status == ReservationStatus.Confirmed)
            .Select(resource => new
            {
                resource.IsExclusive,
                resource.Reservation.StartsAtUtc,
                resource.Reservation.EndsAtUtc
            })
            .ToListAsync(cancellationToken);

        var hasConflict = candidates.Any(candidate =>
            ReservationCompatibility.ConflictsWith(
                candidate.IsExclusive,
                isExclusive,
                candidate.StartsAtUtc,
                candidate.EndsAtUtc,
                command.StartsAtUtc,
                command.EndsAtUtc));

        if (hasConflict)
        {
            throw new ReservationConflictException(
                "The requested time range conflicts with an existing " +
                "incompatible reservation for this amenity.");
        }
    }
}
