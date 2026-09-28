using System.Security.Claims;

namespace ResidentialAmenities.Api.Modules.Identity;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated <c>UserAccount.Id</c>, or null when it cannot be resolved.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;
}
