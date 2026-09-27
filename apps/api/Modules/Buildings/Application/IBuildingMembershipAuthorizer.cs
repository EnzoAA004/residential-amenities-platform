using System.Security.Claims;

namespace ResidentialAmenities.Api.Modules.Buildings.Application;

/// <summary>
/// The Buildings &amp; Memberships module's contract for "does this caller
/// have access to this building". Other modules depend on this instead of
/// querying ResidentMemberships directly, per
/// docs/03-architecture/module-boundaries.md.
/// </summary>
public interface IBuildingMembershipAuthorizer
{
    Task<bool> HasAccessAsync(
        ClaimsPrincipal principal,
        Guid buildingId,
        CancellationToken cancellationToken);
}
