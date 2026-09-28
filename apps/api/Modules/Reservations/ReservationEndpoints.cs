using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Application;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Pricing.Application;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Application;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations;

public static class ReservationEndpoints
{
    public static IEndpointRouteBuilder MapReservationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/reservations")
            .WithTags("Reservations")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        group.MapPost("/", CreateReservationAsync);
        group.MapGet("/{id:guid}", GetReservationAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateReservationAsync(
        CreateReservationRequest request,
        ClaimsPrincipal principal,
        ReservationCreationService creationService,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ReservationUseType>(
                request.UseType,
                ignoreCase: true,
                out var useType))
        {
            return Results.Problem(
                title: "Unable to create this reservation.",
                detail: $"Unknown reservation type '{request.UseType}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The membership is resolved server-side from the authenticated
        // caller; a client-sent membership/user id or price is never
        // trusted (RNF-004).
        var membershipId = await membershipAuthorizer.GetActiveMembershipIdAsync(
            principal,
            request.BuildingId,
            cancellationToken);

        if (membershipId is null)
        {
            return Results.Problem(
                title: "An active resident membership for this building " +
                       "is required to create a reservation.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var actorUserId = principal.GetUserId();

        if (actorUserId is null)
        {
            return Results.Unauthorized();
        }

        var command = new CreateReservationCommand(
            request.BuildingId,
            request.AmenityId,
            request.AddOnAmenityIds ?? [],
            useType,
            request.StartsAtUtc,
            request.EndsAtUtc,
            membershipId.Value,
            actorUserId.Value);

        try
        {
            var reservation = await creationService.CreateAsync(
                command,
                cancellationToken);

            return Results.Created(
                $"/api/reservations/{reservation.Id}",
                ToResponse(reservation));
        }
        catch (ReservationRequestException error)
        {
            return Results.Problem(
                title: "Unable to create this reservation.",
                detail: error.Message,
                statusCode: error.StatusCode);
        }
        catch (ReservationConflictException error)
        {
            return Results.Problem(
                title: "This time range is not available.",
                detail: error.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (PricingException error)
        {
            return Results.Problem(
                title: "Unable to calculate a price quote.",
                detail: error.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<IResult> GetReservationAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations
            .AsNoTracking()
            .Include(candidate => candidate.Resources)
            .Include(candidate => candidate.PriceLines)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);

        if (reservation is null)
        {
            return Results.NotFound();
        }

        if (!await membershipAuthorizer.HasAccessAsync(
                principal,
                reservation.BuildingId,
                cancellationToken))
        {
            return Results.Problem(
                title: "You do not have access to this building.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(ToResponse(reservation));
    }

    private static ReservationResponse ToResponse(Reservation reservation) =>
        new(
            reservation.Id,
            reservation.BuildingId,
            reservation.UseType.ToString(),
            reservation.Status.ToString(),
            reservation.StartsAtUtc,
            reservation.EndsAtUtc,
            reservation.CreatedAtUtc,
            reservation.ExpiresAtUtc,
            reservation.Resources
                .Select(resource => new ReservationResourceResponse(
                    resource.AmenityId,
                    resource.IsExclusive))
                .ToList(),
            reservation.PriceLines
                .Select(line => new ReservationPriceLineResponse(
                    line.AmenityId,
                    line.ComponentType.ToString(),
                    line.Currency,
                    line.Amount))
                .ToList(),
            reservation.PriceLines.FirstOrDefault()?.Currency ?? string.Empty,
            reservation.PriceLines.Sum(line => line.Amount));

    private sealed record CreateReservationRequest(
        Guid BuildingId,
        Guid AmenityId,
        string UseType,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        Guid[]? AddOnAmenityIds = null);

    private sealed record ReservationResponse(
        Guid Id,
        Guid BuildingId,
        string UseType,
        string Status,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        IReadOnlyList<ReservationResourceResponse> Resources,
        IReadOnlyList<ReservationPriceLineResponse> PriceLines,
        string Currency,
        decimal TotalAmount);

    private sealed record ReservationResourceResponse(
        Guid AmenityId,
        bool IsExclusive);

    private sealed record ReservationPriceLineResponse(
        Guid AmenityId,
        string ComponentType,
        string Currency,
        decimal Amount);
}
