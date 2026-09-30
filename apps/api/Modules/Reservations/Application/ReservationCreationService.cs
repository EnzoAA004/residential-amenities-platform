using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Amenities.Application;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Application;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Infrastructure.Persistence;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Orchestrates reservation creation across the modules Reservations
/// depends on (Amenities &amp; Availability, Pricing), per
/// docs/03-architecture/module-boundaries.md. It reuses
/// <see cref="AmenityAvailabilityCalculator"/> and
/// <see cref="PricingCalculator"/> rather than re-implementing either.
///
/// One pipeline serves both Shared/Exclusive Leisure (#20) and Event (#21):
/// each resolves to a list of <see cref="PlannedResource"/> (one for
/// Leisure, base + add-ons for Event), and every later stage — availability,
/// conflict detection, pricing, persistence — operates uniformly over that
/// list instead of duplicating logic per use type.
///
/// RNF-005: the conflict check and the insert happen inside one database
/// transaction, guarded by a <see cref="ResourceAdvisoryLock"/> per
/// requested Amenity (see that type for why advisory locks were chosen over
/// an exclusion constraint or bare Serializable isolation). If any resource
/// fails validation/conflict/pricing, the transaction is never committed —
/// a multi-resource Event never persists a subset of its resources.
/// </summary>
public sealed class ReservationCreationService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<ReservationHoldOptions> holdOptions,
    IAuditRecorder auditRecorder,
    ReservationScheduleValidator scheduleValidator)
{
    private static readonly HashSet<AmenityKind> AllowedEventAddOnKinds =
    [
        AmenityKind.Pool,
        AmenityKind.Barbecue
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

        if (command.UseType is not (ReservationUseType.SharedLeisure
            or ReservationUseType.ExclusiveLeisure
            or ReservationUseType.Event))
        {
            throw new ReservationRequestException(
                $"Reservation type '{command.UseType}' is not supported.",
                StatusCodes.Status400BadRequest);
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == command.BuildingId,
                cancellationToken);

        var resources = command.UseType == ReservationUseType.Event
            ? await PlanEventResourcesAsync(command, building.TimeZoneId, cancellationToken)
            : await PlanLeisureResourceAsync(command, cancellationToken);

        foreach (var resource in resources)
        {
            scheduleValidator.EnsureWithinAvailability(
                resource.Amenity,
                building.TimeZoneId,
                command.StartsAtUtc,
                command.EndsAtUtc);
        }

        // Everything from here on — the conflict check and the insert —
        // runs inside one transaction guarded by a per-Amenity advisory
        // lock, so a concurrent sibling request for the same resource is
        // fully serialized against this one rather than racing it (RNF-005).
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await ResourceAdvisoryLock.AcquireAsync(
            dbContext,
            resources.Select(resource => resource.Amenity.Id),
            cancellationToken);

        var nowUtc = timeProvider.GetUtcNow();

        await scheduleValidator.EnsureNoConflictAsync(
            command.BuildingId,
            resources
                .Select(resource => new ScheduledResource(
                    resource.Amenity.Id,
                    resource.Amenity.Name,
                    resource.IsExclusive))
                .ToList(),
            command.StartsAtUtc,
            command.EndsAtUtc,
            nowUtc,
            excludeReservationId: null,
            cancellationToken);

        var rules = await dbContext.PriceRules
            .AsNoTracking()
            .Where(rule => rule.BuildingId == command.BuildingId)
            .ToListAsync(cancellationToken);

        // PricingException (e.g. no active rule, currency mismatch)
        // propagates to the endpoint, which already knows how to map it to
        // a 422 ProblemDetails response for the /api/pricing/quote endpoint.
        var quote = PricingCalculator.Calculate(
            rules,
            command.AmenityId,
            command.UseType,
            command.AddOnAmenityIds,
            nowUtc);

        var reservation = new Reservation(
            Guid.NewGuid(),
            command.BuildingId,
            command.MembershipId,
            command.UseType,
            command.StartsAtUtc,
            command.EndsAtUtc,
            nowUtc,
            nowUtc + holdOptions.Value.Duration);

        foreach (var resource in resources)
        {
            reservation.AddResource(
                Guid.NewGuid(),
                resource.Amenity.Id,
                resource.IsExclusive);
        }

        foreach (var line in quote.Lines)
        {
            reservation.AddPriceLine(
                Guid.NewGuid(),
                line.PriceRuleId,
                line.AmenityId,
                line.ComponentType,
                line.Currency,
                line.Amount,
                nowUtc);
        }

        // DEC-014/RB-018: SharedLeisure and ExclusiveLeisure are now free.
        // A reservation whose total is exactly zero has nothing for any
        // payment provider to collect, so it is confirmed immediately here
        // rather than waiting on a Mercado Pago or cash payment that will
        // never come — no synthetic/zero-amount Payment record is created
        // for it. This reuses the same trusted-confirmation state
        // transition (`Reservation.Confirm`) that a real payment uses
        // (see `ReservationPaymentContract.ConfirmPaidReservationAsync`),
        // so Reservations still has exactly one path to `Confirmed`.
        var isFree = quote.Lines.Sum(line => line.Amount) == 0m;
        var confirmed = isFree && reservation.Confirm(nowUtc);

        // Nothing is written until this single SaveChanges + commit: an
        // Event that fails any resource's validation, conflict check or
        // pricing never persists another resource partially, and rolling
        // back (including via the `await using` disposing an uncommitted
        // transaction on an exception) also releases the advisory locks.
        dbContext.Reservations.Add(reservation);

        // Same SaveChanges + transaction as the reservation itself: both are
        // persisted together or not at all.
        auditRecorder.Record(AuditRecord.ByUser(
            command.ActorUserId,
            AuditAction.ReservationCreated,
            AuditTargetType.Reservation,
            reservation.Id,
            reservation.BuildingId,
            AuditMetadata.ReservationCreated(
                reservation.UseType.ToString(),
                reservation.StartsAtUtc,
                reservation.EndsAtUtc,
                resources.Count,
                reservation.Status.ToString())));

        if (confirmed)
        {
            auditRecorder.Record(AuditRecord.BySystem(
                AuditAction.ReservationConfirmed,
                AuditTargetType.Reservation,
                reservation.Id,
                reservation.BuildingId,
                AuditMetadata.ReservationConfirmed()));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return reservation;
    }

    private async Task<List<PlannedResource>> PlanLeisureResourceAsync(
        CreateReservationCommand command,
        CancellationToken cancellationToken)
    {
        if (command.AddOnAmenityIds.Count > 0)
        {
            throw new ReservationRequestException(
                "Add-on resources are only supported for Event reservations.",
                StatusCodes.Status400BadRequest);
        }

        var isExclusive = command.UseType == ReservationUseType.ExclusiveLeisure;

        var amenity = await LoadAmenityAsync(
            command.AmenityId,
            command.BuildingId,
            cancellationToken);

        if (amenity is null)
        {
            throw new ReservationRequestException(
                "Amenity not found for this building.",
                StatusCodes.Status404NotFound);
        }

        EnsureActive(amenity);

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

        return [new PlannedResource(amenity, isExclusive)];
    }

    private async Task<List<PlannedResource>> PlanEventResourcesAsync(
        CreateReservationCommand command,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        if (command.AddOnAmenityIds.Contains(command.AmenityId))
        {
            throw new ReservationRequestException(
                "An add-on cannot be the same resource as the base amenity.",
                StatusCodes.Status400BadRequest);
        }

        if (command.AddOnAmenityIds.Count !=
            command.AddOnAmenityIds.Distinct().Count())
        {
            throw new ReservationRequestException(
                "Duplicate add-on amenities are not allowed.",
                StatusCodes.Status400BadRequest);
        }

        var requestedIds = new[] { command.AmenityId }
            .Concat(command.AddOnAmenityIds)
            .ToList();

        var amenities = await dbContext.Amenities
            .Include(candidate => candidate.AvailabilityWindows)
            .Include(candidate => candidate.UnavailablePeriods)
            .Where(candidate =>
                requestedIds.Contains(candidate.Id) &&
                candidate.BuildingId == command.BuildingId)
            .ToDictionaryAsync(
                candidate => candidate.Id,
                cancellationToken);

        if (amenities.Count != requestedIds.Count)
        {
            throw new ReservationRequestException(
                "One or more requested amenities were not found for this " +
                "building.",
                StatusCodes.Status404NotFound);
        }

        var baseAmenity = amenities[command.AmenityId];

        // RB-001: Event reservations require the SUM as the base resource.
        // Deliberately checked against AmenityKind.Sum, never a name/label.
        if (baseAmenity.Kind != AmenityKind.Sum)
        {
            throw new ReservationRequestException(
                "Event reservations require a SUM amenity (AmenityKind.Sum) " +
                "as the base resource.",
                StatusCodes.Status422UnprocessableEntity);
        }

        EnsureActive(baseAmenity);

        if (!baseAmenity.AllowsExclusiveUse)
        {
            throw new ReservationRequestException(
                "The base amenity does not allow exclusive use, which " +
                "Event reservations require (RB-006).",
                StatusCodes.Status422UnprocessableEntity);
        }

        var resources = new List<PlannedResource>
        {
            // Event reserves its base and every add-on exclusively for the
            // whole window (RB-006): the event blocks any other
            // incompatible use of each of its resources, not just the SUM.
            new(baseAmenity, IsExclusive: true)
        };

        foreach (var addOnId in command.AddOnAmenityIds)
        {
            var addOn = amenities[addOnId];

            if (!AllowedEventAddOnKinds.Contains(addOn.Kind))
            {
                throw new ReservationRequestException(
                    $"Amenity kind '{addOn.Kind}' is not a permitted Event " +
                    "add-on. Only Pool and Barbecue are supported.",
                    StatusCodes.Status422UnprocessableEntity);
            }

            EnsureActive(addOn);

            if (!addOn.AllowsExclusiveUse)
            {
                throw new ReservationRequestException(
                    $"Add-on amenity '{addOn.Name}' does not allow " +
                    "exclusive use, which Event add-ons require.",
                    StatusCodes.Status422UnprocessableEntity);
            }

            resources.Add(new PlannedResource(addOn, IsExclusive: true));
        }

        await scheduleValidator.EnsureValidEventSlotAsync(
            command.BuildingId,
            timeZoneId,
            command.StartsAtUtc,
            command.EndsAtUtc,
            cancellationToken);

        return resources;
    }

    private async Task<Amenity?> LoadAmenityAsync(
        Guid amenityId,
        Guid buildingId,
        CancellationToken cancellationToken) =>
        await dbContext.Amenities
            .Include(candidate => candidate.AvailabilityWindows)
            .Include(candidate => candidate.UnavailablePeriods)
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == amenityId &&
                    candidate.BuildingId == buildingId,
                cancellationToken);

    private static void EnsureActive(Amenity amenity)
    {
        if (!amenity.IsActive)
        {
            throw new ReservationRequestException(
                $"Amenity '{amenity.Name}' is not currently active.",
                StatusCodes.Status422UnprocessableEntity);
        }
    }

    private sealed record PlannedResource(Amenity Amenity, bool IsExclusive);
}
