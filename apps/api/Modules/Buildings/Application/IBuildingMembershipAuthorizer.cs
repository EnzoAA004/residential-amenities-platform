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

    /// <summary>
    /// Resolves the caller's own active <c>ResidentMembership.Id</c> for the
    /// building, or null when they have none — including for an
    /// Administrator with no personal membership there. Callers that must
    /// attribute an action to a real membership (e.g. creating a
    /// reservation) use this instead of <see cref="HasAccessAsync"/>, which
    /// intentionally lets Administrators through without one.
    /// </summary>
    Task<Guid?> GetActiveMembershipIdAsync(
        ClaimsPrincipal principal,
        Guid buildingId,
        CancellationToken cancellationToken);
}
