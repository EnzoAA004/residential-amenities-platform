using Microsoft.EntityFrameworkCore;
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
}
