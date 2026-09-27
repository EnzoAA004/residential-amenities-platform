using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Amenities.Application;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Application;
using ResidentialAmenities.Api.Modules.Identity;

namespace ResidentialAmenities.Api.Modules.Amenities;

public static class AmenityEndpoints
{
    public static IEndpointRouteBuilder MapAmenityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api")
            .WithTags("Amenities")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        group.MapGet(
            "/buildings/{buildingId:guid}/amenities",
            ListAmenitiesAsync);

        group.MapGet(
            "/amenities/{amenityId:guid}/availability",
            GetAvailabilityAsync);

        return endpoints;
    }

    private static async Task<IResult> ListAmenitiesAsync(
        Guid buildingId,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken)
    {
        if (!await membershipAuthorizer.HasAccessAsync(
                principal,
                buildingId,
                cancellationToken))
        {
            return Forbidden();
        }

        var amenities = await dbContext.Amenities
            .AsNoTracking()
            .Where(amenity =>
                amenity.BuildingId == buildingId && amenity.IsActive)
            .OrderBy(amenity => amenity.Name)
            .Select(amenity => new AmenitySummaryResponse(
                amenity.Id,
                amenity.Name,
                amenity.Kind.ToString(),
                amenity.AllowsSharedUse,
                amenity.AllowsExclusiveUse))
            .ToListAsync(cancellationToken);

        return Results.Ok(amenities);
    }

    private static async Task<IResult> GetAvailabilityAsync(
        Guid amenityId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken)
    {
        var amenity = await dbContext.Amenities
            .AsNoTracking()
            .Include(candidate => candidate.AvailabilityWindows)
            .Include(candidate => candidate.UnavailablePeriods)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == amenityId,
                cancellationToken);

        if (amenity is null)
        {
            return Results.NotFound();
        }

        if (!await membershipAuthorizer.HasAccessAsync(
                principal,
                amenity.BuildingId,
                cancellationToken))
        {
            return Forbidden();
        }

        var building = await dbContext.Buildings
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == amenity.BuildingId,
                cancellationToken);

        IReadOnlyList<AvailabilityInterval> intervals;

        try
        {
            intervals = AmenityAvailabilityCalculator.CalculateOpenIntervals(
                amenity.AvailabilityWindows,
                amenity.UnavailablePeriods,
                building.TimeZoneId,
                fromUtc,
                toUtc);
        }
        catch (ArgumentException error)
        {
            return Results.Problem(
                title: "Invalid availability query.",
                detail: error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(intervals.Select(
            interval => new AvailabilityIntervalResponse(
                interval.StartUtc,
                interval.EndUtc)));
    }

    private static IResult Forbidden() =>
        Results.Problem(
            title: "You do not have access to this building.",
            statusCode: StatusCodes.Status403Forbidden);

    private sealed record AmenitySummaryResponse(
        Guid Id,
        string Name,
        string Kind,
        bool AllowsSharedUse,
        bool AllowsExclusiveUse);

    private sealed record AvailabilityIntervalResponse(
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc);
}
