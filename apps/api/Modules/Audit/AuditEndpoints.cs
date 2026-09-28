using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Identity;

namespace ResidentialAmenities.Api.Modules.Audit;

/// <summary>
/// Read-only administrative query over the audit trail. There is no
/// create/update/delete endpoint: the trail is append-only and only the
/// application's own use cases write to it.
/// </summary>
public static class AuditEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapAuditEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("/api/admin/audit", QueryAsync)
            .WithTags("Audit")
            .RequireAuthorization(AuthorizationPolicies.Administrator);

        return endpoints;
    }

    private static async Task<IResult> QueryAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken,
        Guid? buildingId = null,
        Guid? actorUserId = null,
        string? action = null,
        string? targetType = null,
        Guid? targetId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int page = 1,
        int pageSize = DefaultPageSize)
    {
        AuditAction? actionFilter = null;
        AuditTargetType? targetTypeFilter = null;

        if (action is not null)
        {
            if (!Enum.TryParse<AuditAction>(action, ignoreCase: true, out var parsed) ||
                !Enum.IsDefined(parsed))
            {
                return Results.Problem(
                    title: "Unknown audit action.",
                    detail: $"'{action}' is not a known audit action.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            actionFilter = parsed;
        }

        if (targetType is not null)
        {
            if (!Enum.TryParse<AuditTargetType>(targetType, ignoreCase: true, out var parsed) ||
                !Enum.IsDefined(parsed))
            {
                return Results.Problem(
                    title: "Unknown audit target type.",
                    detail: $"'{targetType}' is not a known audit target type.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            targetTypeFilter = parsed;
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (buildingId is not null)
        {
            query = query.Where(log => log.BuildingId == buildingId);
        }

        if (actorUserId is not null)
        {
            query = query.Where(log => log.ActorUserId == actorUserId);
        }

        if (actionFilter is not null)
        {
            query = query.Where(log => log.Action == actionFilter);
        }

        if (targetTypeFilter is not null)
        {
            query = query.Where(log => log.TargetType == targetTypeFilter);
        }

        if (targetId is not null)
        {
            query = query.Where(log => log.TargetId == targetId);
        }

        if (fromUtc is not null)
        {
            var from = fromUtc.Value.ToUniversalTime();
            query = query.Where(log => log.OccurredAtUtc >= from);
        }

        if (toUtc is not null)
        {
            var to = toUtc.Value.ToUniversalTime();
            query = query.Where(log => log.OccurredAtUtc < to);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(log => log.OccurredAtUtc)
            .ThenByDescending(log => log.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Results.Ok(new AuditPageResponse(
            rows.Select(ToItem).ToList(),
            page,
            pageSize,
            totalCount));
    }

    private static AuditItemResponse ToItem(AuditLog log) =>
        new(
            log.Id,
            log.OccurredAtUtc,
            log.BuildingId,
            log.ActorType.ToString(),
            log.ActorUserId,
            log.Action.ToString(),
            log.TargetType.ToString(),
            log.TargetId,
            log.CorrelationId,
            log.MetadataJson is null
                ? null
                : JsonSerializer.Deserialize<JsonElement>(log.MetadataJson));

    private sealed record AuditPageResponse(
        IReadOnlyList<AuditItemResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record AuditItemResponse(
        Guid Id,
        DateTimeOffset OccurredAtUtc,
        Guid? BuildingId,
        string ActorType,
        Guid? ActorUserId,
        string Action,
        string TargetType,
        Guid? TargetId,
        string? CorrelationId,
        JsonElement? Metadata);
}
