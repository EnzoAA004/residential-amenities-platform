using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Domain;

namespace ResidentialAmenities.Api.Infrastructure.Persistence;

public static class DevelopmentDataSeeder
{
    public static readonly Guid PilotBuildingId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

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
            Guid.Parse("00000000-0000-0000-0001-000000000001"),
            PilotBuildingId,
            "SUM",
            AmenityKind.Sum,
            allowsSharedUse: true,
            allowsExclusiveUse: true);

        var pool = new Amenity(
            Guid.Parse("00000000-0000-0000-0001-000000000002"),
            PilotBuildingId,
            "Pool",
            AmenityKind.Pool,
            allowsSharedUse: true,
            allowsExclusiveUse: false);

        var barbecue = new Amenity(
            Guid.Parse("00000000-0000-0000-0001-000000000003"),
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
}
