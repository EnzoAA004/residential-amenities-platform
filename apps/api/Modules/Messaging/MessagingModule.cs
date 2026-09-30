using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Application;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Messaging.Domain;

namespace ResidentialAmenities.Api.Modules.Messaging;

/// <summary>
/// Reservation-scoped messaging (#78): a resident sees/writes only the
/// messages on their own reservation, an Administrator sees/writes on any
/// reservation. There is no building-wide channel and no realtime
/// transport — the first slice is plain read (list) + write (post), which
/// the issue explicitly allows for the MVP.
/// </summary>
public static class MessagingModule
{
    public static IServiceCollection AddMessagingModule(
        this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapMessagingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/reservations/{reservationId:guid}/messages")
            .WithTags("Messaging")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        group.MapGet("/", ListAsync);
        group.MapPost("/", PostAsync);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid reservationId,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken)
    {
        var accessResult = await AuthorizeAsync(
            reservationId, principal, dbContext, membershipAuthorizer, cancellationToken);
        if (accessResult.Failure is { } failure)
        {
            return failure;
        }

        var messages = await dbContext.ReservationMessages
            .AsNoTracking()
            .Where(message => message.ReservationId == reservationId)
            .OrderBy(message => message.CreatedAtUtc)
            .Join(
                dbContext.UserAccounts.AsNoTracking(),
                message => message.AuthorUserId,
                user => user.Id,
                (message, user) => new ReservationMessageResponse(
                    message.Id,
                    message.AuthorUserId,
                    user.DisplayName,
                    message.AuthorIsAdministrator,
                    message.Content,
                    message.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(messages);
    }

    private static async Task<IResult> PostAsync(
        Guid reservationId,
        PostReservationMessageRequest request,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var accessResult = await AuthorizeAsync(
            reservationId, principal, dbContext, membershipAuthorizer, cancellationToken);
        if (accessResult.Failure is { } failure)
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["content"] = ["Message content is required."]
            });
        }

        if (request.Content.Trim().Length > ReservationMessage.MaxContentLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["content"] = [$"Message content must be at most {ReservationMessage.MaxContentLength} characters."]
            });
        }

        var userId = principal.GetUserId()!.Value;
        var isAdministrator = principal.IsInRole(ApplicationRoles.Administrator);
        var nowUtc = timeProvider.GetUtcNow();

        var message = new ReservationMessage(
            Guid.NewGuid(),
            reservationId,
            userId,
            isAdministrator,
            request.Content,
            nowUtc);

        dbContext.ReservationMessages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        var author = await dbContext.UserAccounts
            .AsNoTracking()
            .SingleAsync(user => user.Id == userId, cancellationToken);

        return Results.Created(
            $"/api/reservations/{reservationId}/messages",
            new ReservationMessageResponse(
                message.Id,
                message.AuthorUserId,
                author.DisplayName,
                message.AuthorIsAdministrator,
                message.Content,
                message.CreatedAtUtc));
    }

    /// <summary>
    /// Object-level authorization mirrors <c>ReservationEndpoints.GetReservationAsync</c>:
    /// an Administrator can access any reservation's messages; a resident
    /// only their own (by <c>CreatedByMembershipId</c>), never by guessing a
    /// reservation id. Returns 404 for a missing reservation before 403 for
    /// access, so existence is never leaked to an unauthorized caller either
    /// way — both come back indistinguishable to the caller.
    /// </summary>
    private static async Task<AuthorizationResult> AuthorizeAsync(
        Guid reservationId,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is null)
        {
            return AuthorizationResult.Fail(Results.Unauthorized());
        }

        var reservation = await dbContext.Reservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == reservationId,
                cancellationToken);

        if (reservation is null)
        {
            return AuthorizationResult.Fail(Results.NotFound());
        }

        if (principal.IsInRole(ApplicationRoles.Administrator))
        {
            return AuthorizationResult.Ok();
        }

        var membershipId = await membershipAuthorizer.GetActiveMembershipIdAsync(
            principal,
            reservation.BuildingId,
            cancellationToken);

        if (membershipId is null || membershipId != reservation.CreatedByMembershipId)
        {
            return AuthorizationResult.Fail(Results.Problem(
                title: "You do not have access to this reservation's messages.",
                statusCode: StatusCodes.Status403Forbidden));
        }

        return AuthorizationResult.Ok();
    }

    private readonly record struct AuthorizationResult(IResult? Failure)
    {
        public static AuthorizationResult Ok() => new(null);

        public static AuthorizationResult Fail(IResult failure) => new(failure);
    }

    private sealed record PostReservationMessageRequest(string Content);

    private sealed record ReservationMessageResponse(
        Guid Id,
        Guid AuthorUserId,
        string AuthorDisplayName,
        bool AuthorIsAdministrator,
        string Content,
        DateTimeOffset CreatedAtUtc);
}
