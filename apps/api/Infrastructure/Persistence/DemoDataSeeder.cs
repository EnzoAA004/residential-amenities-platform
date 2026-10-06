using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Infrastructure.Persistence;

/// <summary>
/// Creates the isolated resident identity used by the public staging demo.
/// The account intentionally has no password and can only be signed in through
/// the demo-session endpoint while Demo:Enabled is explicitly enabled.
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoResidentEmail = "demo.resident@resamen.local";

    public static readonly Guid DemoResidentUserId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    public static readonly Guid DemoResidentMembershipId =
        Guid.Parse("20000000-0000-0000-0000-000000000002");

    public static async Task SeedDemoDataAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        // Reuse the deterministic pilot catalog (building, units, amenities,
        // pricing and QR entry point) so staging demo sessions have meaningful
        // data without introducing a second copy of those fixtures.
        await services.SeedDevelopmentDataAsync(cancellationToken);

        await using var scope = services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();

        var dbContext =
            scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user =
            await userManager.FindByEmailAsync(DemoResidentEmail);

        if (user is null)
        {
            user = new UserAccount(
                DemoResidentUserId,
                DemoResidentEmail,
                "Demo Resident")
            {
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(user);

            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not create the staging demo resident: " +
                    string.Join(
                        "; ",
                        createResult.Errors.Select(error => error.Description)));
            }
        }

        if (!await userManager.IsInRoleAsync(user, ApplicationRoles.Resident))
        {
            var roleResult =
                await userManager.AddToRoleAsync(
                    user,
                    ApplicationRoles.Resident);

            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not grant the Resident role to the staging demo account: " +
                    string.Join(
                        "; ",
                        roleResult.Errors.Select(error => error.Description)));
            }
        }

        if (await userManager.IsInRoleAsync(
                user,
                ApplicationRoles.Administrator))
        {
            throw new InvalidOperationException(
                "The public demo account must never have Administrator access.");
        }

        var unit = await dbContext.Units
            .SingleAsync(
                candidate =>
                    candidate.BuildingId ==
                    DevelopmentDataSeeder.PilotBuildingId &&
                    candidate.Label == "1A",
                cancellationToken);

        var hasMembership = await dbContext.ResidentMemberships
            .AnyAsync(
                membership =>
                    membership.UserId == user.Id &&
                    membership.BuildingId ==
                    DevelopmentDataSeeder.PilotBuildingId &&
                    membership.Status == ResidentMembershipStatus.Active,
                cancellationToken);

        if (!hasMembership)
        {
            dbContext.ResidentMemberships.Add(
                new ResidentMembership(
                    DemoResidentMembershipId,
                    DevelopmentDataSeeder.PilotBuildingId,
                    unit.Id,
                    user.Id,
                    DateTimeOffset.UtcNow));

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
