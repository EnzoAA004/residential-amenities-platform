using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Application;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Pricing.Application;
using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Modules.Pricing;

public static class PricingEndpoints
{
    public static IEndpointRouteBuilder MapPricingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("/api/pricing/quote", GetQuoteAsync)
            .WithTags("Pricing")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        return endpoints;
    }

    private static async Task<IResult> GetQuoteAsync(
        Guid buildingId,
        Guid amenityId,
        ReservationUseType useType,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken,
        Guid[]? addOnAmenityId = null,
        DateTimeOffset? atUtc = null)
    {
        if (!await membershipAuthorizer.HasAccessAsync(
                principal,
                buildingId,
                cancellationToken))
        {
            return Results.Problem(
                title: "You do not have access to this building.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var quotedAt = atUtc ?? DateTimeOffset.UtcNow;

        var rules = await dbContext.PriceRules
            .AsNoTracking()
            .Where(rule => rule.BuildingId == buildingId)
            .ToListAsync(cancellationToken);

        try
        {
            var quote = PricingCalculator.Calculate(
                rules,
                amenityId,
                useType,
                addOnAmenityId ?? [],
                quotedAt);

            return Results.Ok(quote);
        }
        catch (PricingException error)
        {
            return Results.Problem(
                title: "Unable to calculate a price quote.",
                detail: error.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }
}
