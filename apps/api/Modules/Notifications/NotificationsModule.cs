using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Notifications.Domain;

namespace ResidentialAmenities.Api.Modules.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/notification-subscriptions")
            .WithTags("Notifications")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        group.MapGet("/", ListAsync);
        group.MapPost("/", RegisterAsync);
        group.MapDelete("/{id:guid}", UnregisterAsync);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await dbContext.NotificationSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .OrderByDescending(subscription => subscription.UpdatedAtUtc)
            .Select(subscription => new NotificationSubscriptionResponse(
                subscription.Id,
                subscription.Platform.ToString(),
                subscription.EndpointHash,
                subscription.IsEnabled,
                subscription.CreatedAtUtc,
                subscription.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(subscriptions);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterNotificationSubscriptionRequest request,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<NotificationSubscriptionPlatform>(
                request.Platform,
                ignoreCase: true,
                out var platform))
        {
            return ValidationProblem("platform", "Unsupported notification platform.");
        }

        if (platform != NotificationSubscriptionPlatform.WebPush)
        {
            return ValidationProblem(
                "platform",
                "Only WebPush registration is active until native APNs/FCM setup is configured.");
        }

        if (!IsValidEndpoint(request.Endpoint))
        {
            return ValidationProblem("endpoint", "Endpoint must be an absolute HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(request.P256Dh))
        {
            return ValidationProblem("p256dh", "The p256dh key is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Auth))
        {
            return ValidationProblem("auth", "The auth secret is required.");
        }

        var nowUtc = timeProvider.GetUtcNow();
        var endpoint = request.Endpoint.Trim();
        var endpointHash = HashEndpoint(endpoint);

        var subscription = await dbContext.NotificationSubscriptions
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.UserId == userId &&
                    candidate.EndpointHash == endpointHash,
                cancellationToken);

        if (subscription is null)
        {
            subscription = new NotificationSubscription(
                Guid.NewGuid(),
                userId,
                platform,
                endpoint,
                endpointHash,
                request.P256Dh,
                request.Auth,
                request.UserAgent,
                nowUtc);
            dbContext.NotificationSubscriptions.Add(subscription);
        }
        else
        {
            subscription.Refresh(
                platform,
                endpoint,
                request.P256Dh,
                request.Auth,
                request.UserAgent,
                nowUtc);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(subscription));
    }

    private static async Task<IResult> UnregisterAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        var subscription = await dbContext.NotificationSubscriptions
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id && candidate.UserId == userId,
                cancellationToken);

        if (subscription is null)
        {
            return Results.NotFound();
        }

        dbContext.NotificationSubscriptions.Remove(subscription);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static NotificationSubscriptionResponse ToResponse(
        NotificationSubscription subscription) =>
        new(
            subscription.Id,
            subscription.Platform.ToString(),
            subscription.EndpointHash,
            subscription.IsEnabled,
            subscription.CreatedAtUtc,
            subscription.UpdatedAtUtc);

    private static bool IsValidEndpoint(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;

    private static string HashEndpoint(string endpoint)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(endpoint));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IResult ValidationProblem(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [key] = [message]
        });

    private sealed record RegisterNotificationSubscriptionRequest(
        string Platform,
        string Endpoint,
        string P256Dh,
        string Auth,
        string? UserAgent);

    private sealed record NotificationSubscriptionResponse(
        Guid Id,
        string Platform,
        string EndpointHash,
        bool IsEnabled,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
