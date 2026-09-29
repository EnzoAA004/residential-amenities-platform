using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Application;
using ResidentialAmenities.Api.Modules.Identity;

namespace ResidentialAmenities.Api.Modules.Reservations;

public static class ReservationEntryPointEndpoints
{
    public static IEndpointRouteBuilder MapReservationEntryPointEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/reservation-entry-points/{token}",
                GetEntryPointAsync)
            .WithTags("Reservations")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        return endpoints;
    }

    private static async Task<IResult> GetEntryPointAsync(
        string token,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.NotFound();
        }

        var normalizedToken = token.Trim().ToLowerInvariant();

        var entryPoint = await dbContext.ReservationEntryPoints
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Token == normalizedToken,
                cancellationToken);

        if (entryPoint is null || !entryPoint.IsActive)
        {
            return Results.NotFound();
        }

        if (!await membershipAuthorizer.HasAccessAsync(
                principal,
                entryPoint.BuildingId,
                cancellationToken))
        {
            return Results.Problem(
                title: "You do not have access to this building.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var amenity = await dbContext.Amenities
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == entryPoint.AmenityId &&
                    candidate.BuildingId == entryPoint.BuildingId,
                cancellationToken);

        if (amenity is null || !amenity.IsActive)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ReservationEntryPointResponse(
            entryPoint.Token,
            entryPoint.BuildingId,
            amenity.Id,
            entryPoint.DisplayName ?? amenity.Name,
            amenity.Name,
            amenity.Kind.ToString(),
            amenity.AllowsSharedUse,
            amenity.AllowsExclusiveUse,
            entryPoint.SuggestedUseType?.ToString()));
    }

    private sealed record ReservationEntryPointResponse(
        string Token,
        Guid BuildingId,
        Guid AmenityId,
        string DisplayName,
        string AmenityName,
        string AmenityKind,
        bool AllowsSharedUse,
        bool AllowsExclusiveUse,
        string? SuggestedUseType);
}
