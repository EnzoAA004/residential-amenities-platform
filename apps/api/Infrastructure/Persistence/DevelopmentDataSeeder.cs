using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Infrastructure.Persistence;

public static class DevelopmentDataSeeder
{
    public static readonly Guid PilotBuildingId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static readonly Guid PilotSumId =
        Guid.Parse("00000000-0000-0000-0001-000000000001");

    public static readonly Guid PilotPoolId =
        Guid.Parse("00000000-0000-0000-0001-000000000002");

    public static readonly Guid PilotBarbecueId =
        Guid.Parse("00000000-0000-0000-0001-000000000003");

    public static async Task SeedDevelopmentDataAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var building = await dbContext.Buildings
            .SingleOrDefaultAsync(
                candidate => candidate.Id == PilotBuildingId,
                cancellationToken);

        if (building is null)
        {
            building = new Building(
                PilotBuildingId,
                "Pilot Building",
                "America/Argentina/Buenos_Aires");

            dbContext.Buildings.Add(building);
        }

        var existingLabels = await dbContext.Units
            .Where(unit => unit.BuildingId == PilotBuildingId)
            .Select(unit => unit.Label)
            .ToHashSetAsync(cancellationToken);

        foreach (var unit in CreatePilotUnits())
        {
            if (!existingLabels.Contains(unit.Label))
            {
                dbContext.Units.Add(unit);
            }
        }

        var existingAmenityNames = await dbContext.Amenities
            .Where(amenity => amenity.BuildingId == PilotBuildingId)
            .Select(amenity => amenity.Name)
            .ToHashSetAsync(cancellationToken);

        foreach (var amenity in CreatePilotAmenities())
        {
            if (!existingAmenityNames.Contains(amenity.Name))
            {
                dbContext.Amenities.Add(amenity);
            }
        }

        var hasPriceRules = await dbContext.PriceRules
            .AnyAsync(
                rule => rule.BuildingId == PilotBuildingId,
                cancellationToken);

        if (!hasPriceRules)
        {
            dbContext.PriceRules.AddRange(CreatePilotPriceRules());
        }

        var hasEventSlots = await dbContext.EventSlotDefinitions
            .AnyAsync(
                slot => slot.BuildingId == PilotBuildingId,
                cancellationToken);

        if (!hasEventSlots)
        {
            dbContext.EventSlotDefinitions.AddRange(CreatePilotEventSlots());
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<Unit> CreatePilotUnits()
    {
        var ordinal = 1;

        for (var floor = 1; floor <= 5; floor++)
        {
            foreach (var door in new[] { "A", "B" })
            {
                yield return new Unit(
                    Guid.Parse(
                        $"00000000-0000-0000-0000-{ordinal:000000000000}"),
                    PilotBuildingId,
                    floor,
                    door,
                    $"{floor}{door}");

                ordinal++;
            }
        }
    }

    // Placeholder daily windows only, pending business-hours validation in
    // issue #2 (docs/01-discovery/assumptions-and-open-questions.md). These
    // exist so the availability endpoint has something to query locally;
    // they are not a final product decision.
    private static IEnumerable<Amenity> CreatePilotAmenities()
    {
        var sum = new Amenity(
            PilotSumId,
            PilotBuildingId,
            "SUM",
            AmenityKind.Sum,
            allowsSharedUse: true,
            allowsExclusiveUse: true);

        // AllowsExclusiveUse is true (not just shared) so Pool can also be
        // booked as an exclusive Event add-on (issue #21); a plain
        // resident-facing exclusive Pool booking is not implemented yet
        // (OQ-014, still open).
        var pool = new Amenity(
            PilotPoolId,
            PilotBuildingId,
            "Pool",
            AmenityKind.Pool,
            allowsSharedUse: true,
            allowsExclusiveUse: true);

        var barbecue = new Amenity(
            PilotBarbecueId,
            PilotBuildingId,
            "Barbecue",
            AmenityKind.Barbecue,
            allowsSharedUse: false,
            allowsExclusiveUse: true);

        foreach (var amenity in new[] { sum, pool, barbecue })
        {
            for (var day = DayOfWeek.Sunday;
                 day <= DayOfWeek.Saturday;
                 day++)
            {
                amenity.AddAvailabilityWindow(
                    Guid.NewGuid(),
                    day,
                    new TimeOnly(9, 0),
                    new TimeOnly(22, 0));
            }
        }

        return [sum, pool, barbecue];
    }

    // Placeholder pricing only, pending the final pricing decisions in
    // issue #2 (RB-016). These exist so PricingCalculator has something to
    // query locally; amounts/currency are not a final product decision.
    private static IEnumerable<PriceRule> CreatePilotPriceRules()
    {
        var effectiveFrom = new DateTimeOffset(
            2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        yield return new PriceRule(
            Guid.NewGuid(),
            PilotBuildingId,
            PilotSumId,
            PriceComponentType.Base,
            ReservationUseType.SharedLeisure,
            "ARS",
            5_000m,
            effectiveFrom,
            null);

        yield return new PriceRule(
            Guid.NewGuid(),
            PilotBuildingId,
            PilotSumId,
            PriceComponentType.Base,
            ReservationUseType.ExclusiveLeisure,
            "ARS",
            8_000m,
            effectiveFrom,
            null);

        yield return new PriceRule(
            Guid.NewGuid(),
            PilotBuildingId,
            PilotSumId,
            PriceComponentType.Base,
            ReservationUseType.Event,
            "ARS",
            15_000m,
            effectiveFrom,
            null);

        yield return new PriceRule(
            Guid.NewGuid(),
            PilotBuildingId,
            PilotPoolId,
            PriceComponentType.AddOn,
            ReservationUseType.Event,
            "ARS",
            3_000m,
            effectiveFrom,
            null);

        yield return new PriceRule(
            Guid.NewGuid(),
            PilotBuildingId,
            PilotBarbecueId,
            PriceComponentType.AddOn,
            ReservationUseType.Event,
            "ARS",
            3_000m,
            effectiveFrom,
            null);
    }

    // Placeholder Event slots only. "Afternoon"/"Evening" naming and exact
    // boundaries are explicitly TBD pending issue #2 (OQ-001/OQ-002 in
    // docs/01-discovery/assumptions-and-open-questions.md); full-day is not
    // seeded because whether it belongs in the MVP is still open (OQ-003).
    private static IEnumerable<EventSlotDefinition> CreatePilotEventSlots()
    {
        yield return new EventSlotDefinition(
            Guid.NewGuid(),
            PilotBuildingId,
            "Placeholder afternoon slot",
            new TimeOnly(14, 0),
            new TimeOnly(19, 0));

        // Kept within the amenities' seeded 09:00-22:00 general
        // availability window (docs/04-data/data-dictionary.md) so this
        // slot is actually bookable; a later, real evening/night boundary
        // may need that general window widened too once issue #2 answers
        // OQ-002.
        yield return new EventSlotDefinition(
            Guid.NewGuid(),
            PilotBuildingId,
            "Placeholder evening slot",
            new TimeOnly(19, 0),
            new TimeOnly(22, 0));
    }
}
