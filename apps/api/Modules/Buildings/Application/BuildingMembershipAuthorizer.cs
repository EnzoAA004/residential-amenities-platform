using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Modules.Buildings.Application;

public sealed class BuildingMembershipAuthorizer(
    AppDbContext dbContext,
    UserManager<UserAccount> userManager)
    : IBuildingMembershipAuthorizer
{
    public async Task<bool> HasAccessAsync(
        ClaimsPrincipal principal,
        Guid buildingId,
        CancellationToken cancellationToken)
    {
        if (principal.IsInRole(ApplicationRoles.Administrator))
        {
            return true;
        }

        var user = await userManager.GetUserAsync(principal);

        if (user is null)
        {
            return false;
        }

        return await dbContext.ResidentMemberships
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.UserId == user.Id &&
                    membership.BuildingId == buildingId &&
                    membership.Status == ResidentMembershipStatus.Active,
                cancellationToken);
    }

    public async Task<Guid?> GetActiveMembershipIdAsync(
        ClaimsPrincipal principal,
        Guid buildingId,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);

        if (user is null)
        {
            return null;
        }

        return await dbContext.ResidentMemberships
            .AsNoTracking()
            .Where(
                membership =>
                    membership.UserId == user.Id &&
                    membership.BuildingId == buildingId &&
                    membership.Status == ResidentMembershipStatus.Active)
            .Select(membership => (Guid?)membership.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
