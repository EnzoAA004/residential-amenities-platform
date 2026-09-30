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
        group.MapGet("/", ListMyReservationsAsync);
        group.MapGet("/shared-occupancy", GetSharedOccupancyAsync);
        group.MapGet("/{id:guid}", GetReservationAsync);

        return endpoints;
    }

    /// <summary>
    /// Issue #89: before a resident confirms a SharedLeisure reservation,
    /// they must see which units already hold an overlapping SharedLeisure
    /// booking for the same amenity — DEC-014/OQ-009 removed any capacity
    /// cap, so this is informational only, never a rejection. Returns unit
    /// display labels only (e.g. "1A") — never a name, email, user id,
    /// membership id or phone, from this endpoint or any other one reused
    /// for this purpose (this is a purpose-built, minimal read, not an
    /// extension of an endpoint that carries membership/user data).
    /// </summary>
    private static async Task<IResult> GetSharedOccupancyAsync(
        Guid buildingId,
        Guid amenityId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (buildingId == Guid.Empty || amenityId == Guid.Empty)
        {
            return Results.Problem(
                title: "buildingId and amenityId are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (endsAtUtc <= startsAtUtc)
        {
            return Results.Problem(
                title: "endsAtUtc must be after startsAtUtc.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await membershipAuthorizer.HasAccessAsync(principal, buildingId, cancellationToken))
        {
            return Results.Problem(
                title: "You do not have access to this building.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var amenityExists = await dbContext.Amenities.AnyAsync(
            amenity => amenity.Id == amenityId && amenity.BuildingId == buildingId,
            cancellationToken);

        if (!amenityExists)
        {
            return Results.NotFound();
        }

        var nowUtc = timeProvider.GetUtcNow();

        var unitLabels = await dbContext.ReservationResources
            .Where(resource => resource.AmenityId == amenityId)
            .Join(
                dbContext.Reservations,
                resource => resource.ReservationId,
                reservation => reservation.Id,
                (resource, reservation) => reservation)
            .Where(reservation =>
                reservation.BuildingId == buildingId &&
                reservation.UseType == ReservationUseType.SharedLeisure &&
                (reservation.Status == ReservationStatus.Confirmed ||
                 (reservation.Status == ReservationStatus.Pending &&
                  reservation.ExpiresAtUtc > nowUtc)) &&
                reservation.StartsAtUtc < endsAtUtc &&
                startsAtUtc < reservation.EndsAtUtc)
            .Join(
                dbContext.ResidentMemberships,
                reservation => reservation.CreatedByMembershipId,
                membership => membership.Id,
                (reservation, membership) => membership.UnitId)
            .Join(
                dbContext.Units,
                unitId => unitId,
                unit => unit.Id,
                (unitId, unit) => unit.Label)
            .Distinct()
            .OrderBy(label => label)
            .ToListAsync(cancellationToken);

        return Results.Ok(new SharedOccupancyResponse(unitLabels));
    }

    private sealed record SharedOccupancyResponse(IReadOnlyList<string> UnitLabels);

    /// <summary>
    /// "My reservations" (issue #66): reservations created by the caller's
    /// own active membership in <paramref name="buildingId"/> — never a
    /// general building listing, and never accepting a membership/user id
    /// from the client (RNF-004). <paramref name="buildingId"/> is required;
    /// an unauthenticated-for-this-building caller (no active membership)
    /// gets 403, not an empty page, so it is never confused with "no
    /// reservations yet".
    /// </summary>
    private static async Task<IResult> ListMyReservationsAsync(
        Guid buildingId,
        ClaimsPrincipal principal,
        IResidentReservationQuery query,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = ReservationAdminQuery.DefaultPageSize)
    {
        if (buildingId == Guid.Empty)
        {
            return Results.Problem(
                title: "buildingId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var membershipId = await membershipAuthorizer.GetActiveMembershipIdAsync(
            principal,
            buildingId,
            cancellationToken);

        if (membershipId is null)
        {
            return Results.Problem(
                title: "An active resident membership for this building " +
                       "is required to list reservations.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await query.ListAsync(
            buildingId,
            membershipId.Value,
            page,
            pageSize,
            cancellationToken);

        return Results.Ok(new ResidentReservationPageResponse(
            result.Items.Select(ToSummary).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount));
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

        // Building-level access is not enough here: two residents of the
        // same building must not be able to read each other's reservation by
        // guessing/sharing its id (object-level authorization). An
        // Administrator keeps the existing full-access bypass.
        if (!principal.IsInRole(ApplicationRoles.Administrator))
        {
            var membershipId = await membershipAuthorizer.GetActiveMembershipIdAsync(
                principal,
                reservation.BuildingId,
                cancellationToken);

            if (membershipId is null || membershipId != reservation.CreatedByMembershipId)
            {
                return Results.Problem(
                    title: "You do not have access to this reservation.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
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
            reservation.ConfirmedAtUtc,
            reservation.CancelledAtUtc,
            reservation.ExpiredAtUtc,
            reservation.CancellationReason,
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

    // Reservation.Status remains the sole authority: nothing here infers
    // Expired/Confirmed from the clock or from a payment — every lifecycle
    // field is copied straight from the stored aggregate.
    private static ResidentReservationSummaryResponse ToSummary(ResidentReservationRow row) =>
        new(
            row.Id,
            row.BuildingId,
            row.UseType,
            row.Status,
            row.StartsAtUtc,
            row.EndsAtUtc,
            row.CreatedAtUtc,
            row.ExpiresAtUtc,
            row.ConfirmedAtUtc,
            row.CancelledAtUtc,
            row.ExpiredAtUtc,
            row.CancellationReason,
            row.Resources
                .Select(resource => new ReservationResourceResponse(
                    resource.AmenityId,
                    resource.IsExclusive))
                .ToList(),
            row.Currency ?? string.Empty,
            row.Total);

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
        DateTimeOffset? ConfirmedAtUtc,
        DateTimeOffset? CancelledAtUtc,
        DateTimeOffset? ExpiredAtUtc,
        string? CancellationReason,
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

    /// <summary>
    /// The "my reservations" list item. Never includes CreatedByMembershipId,
    /// any user id, or price line detail — the resident already knows these
    /// are their own reservations; per-line pricing stays on the detail
    /// endpoint.
    /// </summary>
    private sealed record ResidentReservationSummaryResponse(
        Guid Id,
        Guid BuildingId,
        string UseType,
        string Status,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? ConfirmedAtUtc,
        DateTimeOffset? CancelledAtUtc,
        DateTimeOffset? ExpiredAtUtc,
        string? CancellationReason,
        IReadOnlyList<ReservationResourceResponse> Resources,
        string Currency,
        decimal TotalAmount);

    private sealed record ResidentReservationPageResponse(
        IReadOnlyList<ResidentReservationSummaryResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);
}
