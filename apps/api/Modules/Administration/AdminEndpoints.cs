using System.Security.Claims;
using ResidentialAmenities.Api.Modules.Amenities.Application;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Payments.Application;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Application;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Application;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Administration;

/// <summary>
/// The administrative HTTP surface (RF-017..RF-020). Administration only
/// authenticates the caller, maps requests/responses and delegates: every
/// state change is a command on the module that owns the data, which enforces
/// its own invariants. The actor always comes from the session, never from the
/// request. (The audit query lives at <c>/api/admin/audit</c> in the Audit
/// module and cash confirmation at <c>/api/payments/{id}/cash/confirm</c> in
/// Payments; neither is duplicated here.)
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/admin")
            .WithTags("Administration")
            .RequireAuthorization(AuthorizationPolicies.Administrator);

        group.MapGet("/reservations", ListReservationsAsync);
        group.MapGet("/reservations/{id:guid}", GetReservationAsync);
        group.MapPost("/reservations/{id:guid}/cancel", CancelReservationAsync);
        group.MapPost("/reservations/{id:guid}/reschedule", RescheduleReservationAsync);

        group.MapGet("/payments", ListPaymentsAsync);

        group.MapGet("/pricing/rules", ListPriceRulesAsync);
        group.MapPost("/pricing/rules", CreatePriceRuleAsync);

        group.MapGet("/amenities/{amenityId:guid}/availability", GetAvailabilityAsync);
        group.MapPut("/amenities/{amenityId:guid}/availability", ReplaceAvailabilityAsync);
        group.MapPost("/amenities/{amenityId:guid}/unavailable-periods", AddUnavailablePeriodAsync);
        group.MapDelete(
            "/amenities/{amenityId:guid}/unavailable-periods/{periodId:guid}",
            RemoveUnavailablePeriodAsync);

        group.MapGet("/buildings/{buildingId:guid}/event-slots", ListEventSlotsAsync);
        group.MapPost("/buildings/{buildingId:guid}/event-slots", CreateEventSlotAsync);
        group.MapPut("/event-slots/{id:guid}", UpdateEventSlotAsync);
        group.MapPost("/event-slots/{id:guid}/deactivate", DeactivateEventSlotAsync);
        group.MapPost("/event-slots/{id:guid}/activate", ActivateEventSlotAsync);

        return endpoints;
    }

    // --- reservations -------------------------------------------------------------

    private static async Task<IResult> ListReservationsAsync(
        AdminReservationReadService reads,
        CancellationToken cancellationToken,
        Guid? buildingId = null,
        string? status = null,
        string? useType = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        Guid? membershipId = null,
        int page = 1,
        int pageSize = ReservationAdminQuery.DefaultPageSize)
    {
        ReservationStatus? statusFilter = null;
        ReservationUseType? useTypeFilter = null;

        if (status is not null)
        {
            if (!Enum.TryParse<ReservationStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return BadRequest($"'{status}' is not a known reservation status.");
            }

            statusFilter = parsed;
        }

        if (useType is not null)
        {
            if (!Enum.TryParse<ReservationUseType>(useType, true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return BadRequest($"'{useType}' is not a known reservation type.");
            }

            useTypeFilter = parsed;
        }

        return Results.Ok(await reads.ListAsync(
            new AdminReservationFilter(
                buildingId, statusFilter, useTypeFilter, fromUtc, toUtc, membershipId, page, pageSize),
            cancellationToken));
    }

    private static async Task<IResult> GetReservationAsync(
        Guid id,
        AdminReservationReadService reads,
        CancellationToken cancellationToken)
    {
        var detail = await reads.GetAsync(id, cancellationToken);

        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> CancelReservationAsync(
        Guid id,
        CancelReservationRequest request,
        ClaimsPrincipal principal,
        IReservationAdminContract commands,
        AdminReservationReadService reads,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } actorUserId)
        {
            return Results.Unauthorized();
        }

        try
        {
            var outcome = await commands.CancelAsync(id, request.Reason, actorUserId, cancellationToken);

            return outcome switch
            {
                ReservationCancelOutcome.NotFound => Results.NotFound(),
                ReservationCancelOutcome.NotCancellable => Results.Problem(
                    title: "Unable to cancel this reservation.",
                    detail: "An expired reservation cannot be cancelled.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Ok(await reads.GetAsync(id, cancellationToken))
            };
        }
        catch (Exception error) when (ToProblem(error) is { } problem)
        {
            return problem;
        }
    }

    private static async Task<IResult> RescheduleReservationAsync(
        Guid id,
        RescheduleReservationRequest request,
        ClaimsPrincipal principal,
        IReservationAdminContract commands,
        AdminReservationReadService reads,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } actorUserId)
        {
            return Results.Unauthorized();
        }

        try
        {
            await commands.RescheduleAsync(
                id,
                request.StartsAtUtc,
                request.EndsAtUtc,
                request.Reason,
                actorUserId,
                cancellationToken);

            return Results.Ok(await reads.GetAsync(id, cancellationToken));
        }
        catch (Exception error) when (ToProblem(error) is { } problem)
        {
            return problem;
        }
    }

    // --- payments (read-only) -----------------------------------------------------

    private static async Task<IResult> ListPaymentsAsync(
        IPaymentAdminQuery payments,
        CancellationToken cancellationToken,
        Guid? buildingId = null,
        string? method = null,
        string? status = null,
        bool? requiresManualReview = null,
        Guid? reservationId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int page = 1,
        int pageSize = 50)
    {
        PaymentMethod? methodFilter = null;
        PaymentStatus? statusFilter = null;

        if (method is not null)
        {
            if (!Enum.TryParse<PaymentMethod>(method, true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return BadRequest($"'{method}' is not a known payment method.");
            }

            methodFilter = parsed;
        }

        if (status is not null)
        {
            if (!Enum.TryParse<PaymentStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return BadRequest($"'{status}' is not a known payment status.");
            }

            statusFilter = parsed;
        }

        return Results.Ok(await payments.ListAsync(
            new AdminPaymentFilter(
                buildingId,
                methodFilter,
                statusFilter,
                requiresManualReview,
                reservationId,
                fromUtc,
                toUtc,
                page,
                pageSize),
            cancellationToken));
    }

    // --- pricing ------------------------------------------------------------------

    private static async Task<IResult> ListPriceRulesAsync(
        Guid buildingId,
        IPricingAdminContract pricing,
        CancellationToken cancellationToken,
        Guid? amenityId = null,
        DateTimeOffset? activeAtUtc = null,
        int page = 1,
        int pageSize = 50) =>
        Results.Ok(await pricing.ListRulesAsync(
            buildingId, amenityId, activeAtUtc, page, pageSize, cancellationToken));

    private static async Task<IResult> CreatePriceRuleAsync(
        CreatePriceRuleRequest request,
        ClaimsPrincipal principal,
        IPricingAdminContract pricing,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } actorUserId)
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<PriceComponentType>(request.ComponentType, true, out var componentType) ||
            !Enum.TryParse<ReservationUseType>(request.UseType, true, out var useType))
        {
            return BadRequest("Unknown component type or use type.");
        }

        try
        {
            var result = await pricing.CreateRuleAsync(
                new CreatePriceRuleCommand(
                    request.BuildingId,
                    request.AmenityId,
                    componentType,
                    useType,
                    request.Currency,
                    request.Amount,
                    request.EffectiveFromUtc,
                    request.EffectiveToUtc),
                actorUserId,
                cancellationToken);

            return Results.Created($"/api/admin/pricing/rules?buildingId={request.BuildingId}", result);
        }
        catch (Exception error) when (ToProblem(error) is { } problem)
        {
            return problem;
        }
    }

    // --- availability -------------------------------------------------------------

    private static async Task<IResult> GetAvailabilityAsync(
        Guid amenityId,
        Guid buildingId,
        IAmenityAdminContract amenities,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await amenities.GetAvailabilityAsync(buildingId, amenityId, cancellationToken));
        }
        catch (Exception error) when (ToProblem(error) is { } problem)
        {
            return problem;
        }
    }

    private static async Task<IResult> ReplaceAvailabilityAsync(
        Guid amenityId,
        ReplaceAvailabilityRequest request,
        ClaimsPrincipal principal,
        IAmenityAdminContract amenities,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } actorUserId)
        {
            return Results.Unauthorized();
        }

        try
        {
            await amenities.ReplaceAvailabilityAsync(
                request.BuildingId,
                amenityId,
                (request.Windows ?? [])
                    .Select(window => new AvailabilityWindowInput(
                        window.DayOfWeek, window.StartTime, window.EndTime))
                    .ToList(),
                actorUserId,
                cancellationToken);

            return Results.Ok(await amenities.GetAvailabilityAsync(
                request.BuildingId, amenityId, cancellationToken));
        }
        catch (Exception error) when (ToProblem(error) is { } problem)
        {
            return problem;
        }
    }

    private static async Task<IResult> AddUnavailablePeriodAsync(
        Guid amenityId,
        UnavailablePeriodRequest request,
        ClaimsPrincipal principal,
        IAmenityAdminContract amenities,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } actorUserId)
        {
            return Results.Unauthorized();
        }

        try
        {
            var periodId = await amenities.AddUnavailablePeriodAsync(
                request.BuildingId,
                amenityId,
                request.StartsAtUtc,
                request.EndsAtUtc,
                request.Reason,
                actorUserId,
                cancellationToken);

            return Results.Created(
                $"/api/admin/amenities/{amenityId}/availability?buildingId={request.BuildingId}",
                new { periodId });
        }
        catch (Exception error) when (ToProblem(error) is { } problem)
        {
            return problem;
        }
    }

    private static async Task<IResult> RemoveUnavailablePeriodAsync(
        Guid amenityId,
        Guid periodId,
        Guid buildingId,
        ClaimsPrincipal principal,
        IAmenityAdminContract amenities,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } actorUserId)
        {
            return Results.Unauthorized();
        }

        try
        {
            await amenities.RemoveUnavailablePeriodAsync(
                buildingId, amenityId, periodId, actorUserId, cancellationToken);

            return Results.NoContent();
        }
        catch (Exception error) when (ToProblem(error) is { } problem)
        {
            return problem;
        }
    }

    // --- event slots --------------------------------------------------------------

    private static Task<IResult> ListEventSlotsAsync(
        Guid buildingId,
        IEventSlotAdminContract slots,
        CancellationToken cancellationToken) =>
        SlotAsync(async () => Results.Ok(await slots.ListAsync(buildingId, cancellationToken)));

    private static Task<IResult> CreateEventSlotAsync(
        Guid buildingId,
        EventSlotRequest request,
        ClaimsPrincipal principal,
        IEventSlotAdminContract slots,
        CancellationToken cancellationToken) =>
        principal.GetUserId() is not { } actorUserId
            ? Task.FromResult(Results.Unauthorized())
            : SlotAsync(async () =>
            {
                var created = await slots.CreateAsync(
                    buildingId, request.Name, request.StartTime, request.EndTime,
                    request.IsOvernight, actorUserId, cancellationToken);

                return Results.Created($"/api/admin/event-slots/{created.Id}", created);
            });

    private static Task<IResult> UpdateEventSlotAsync(
        Guid id,
        EventSlotRequest request,
        ClaimsPrincipal principal,
        IEventSlotAdminContract slots,
        CancellationToken cancellationToken) =>
        principal.GetUserId() is not { } actorUserId
            ? Task.FromResult(Results.Unauthorized())
            : SlotAsync(async () => Results.Ok(await slots.UpdateAsync(
                id, request.Name, request.StartTime, request.EndTime,
                request.IsOvernight, actorUserId, cancellationToken)));

    private static Task<IResult> DeactivateEventSlotAsync(
        Guid id,
        ClaimsPrincipal principal,
        IEventSlotAdminContract slots,
        CancellationToken cancellationToken) =>
        principal.GetUserId() is not { } actorUserId
            ? Task.FromResult(Results.Unauthorized())
            : SlotAsync(async () => Results.Ok(
                await slots.DeactivateAsync(id, actorUserId, cancellationToken)));

    private static Task<IResult> ActivateEventSlotAsync(
        Guid id,
        ClaimsPrincipal principal,
        IEventSlotAdminContract slots,
        CancellationToken cancellationToken) =>
        principal.GetUserId() is not { } actorUserId
            ? Task.FromResult(Results.Unauthorized())
            : SlotAsync(async () => Results.Ok(
                await slots.ActivateAsync(id, actorUserId, cancellationToken)));

    private static async Task<IResult> SlotAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception error) when (ToProblem(error) is { } problem)
        {
            return problem;
        }
    }

    // --- errors -------------------------------------------------------------------

    private static IResult BadRequest(string detail) =>
        Results.Problem(
            title: "Invalid request.",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Maps the owning modules' business exceptions to ProblemDetails; anything else is not handled here.</summary>
    private static IResult? ToProblem(Exception error) =>
        error switch
        {
            ReservationRequestException request => Results.Problem(
                title: "Unable to complete this operation.",
                detail: request.Message,
                statusCode: request.StatusCode),
            ReservationConflictException conflict => Results.Problem(
                title: "This time range is not available.",
                detail: conflict.Message,
                statusCode: StatusCodes.Status409Conflict),
            PricingAdminException pricing => Results.Problem(
                title: "Unable to complete this operation.",
                detail: pricing.Message,
                statusCode: pricing.StatusCode),
            AmenityRequestException amenity => Results.Problem(
                title: "Unable to complete this operation.",
                detail: amenity.Message,
                statusCode: amenity.StatusCode),
            _ => null
        };

    private sealed record CancelReservationRequest(string Reason);

    private sealed record RescheduleReservationRequest(
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        string Reason);

    private sealed record CreatePriceRuleRequest(
        Guid BuildingId,
        Guid AmenityId,
        string ComponentType,
        string UseType,
        string Currency,
        decimal Amount,
        DateTimeOffset? EffectiveFromUtc,
        DateTimeOffset? EffectiveToUtc);

    private sealed record AvailabilityWindowRequest(
        DayOfWeek DayOfWeek,
        TimeOnly StartTime,
        TimeOnly EndTime);

    private sealed record ReplaceAvailabilityRequest(
        Guid BuildingId,
        List<AvailabilityWindowRequest>? Windows);

    private sealed record UnavailablePeriodRequest(
        Guid BuildingId,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        string? Reason);

    private sealed record EventSlotRequest(
        string Name,
        TimeOnly StartTime,
        TimeOnly EndTime,
        bool IsOvernight = false);
}
