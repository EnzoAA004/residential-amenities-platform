using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Application;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Reports.Domain;

namespace ResidentialAmenities.Api.Modules.Reports;

/// <summary>
/// Incident/damage reports (issue #91, DEC-014/OQ-012) — evidence for an
/// Administrator to review manually. Deliberately a separate section from
/// #78's reservation messaging, even though both reuse the same
/// authentication/authorization primitives: a report is building-scoped
/// (an Administrator reviews every report for their building) with an
/// optional reservation reference, never a building-wide chat and never a
/// per-reservation-only view. Nothing here calculates or charges a penalty;
/// any consequence is a manual, out-of-band administrative decision.
/// </summary>
public static class ReportsModule
{
    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/reports")
            .WithTags("Reports")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListMineAsync);
        group.MapGet("/{id:guid}", GetMineAsync);

        var adminGroup = endpoints
            .MapGroup("/api/admin/reports")
            .WithTags("Reports")
            .RequireAuthorization(AuthorizationPolicies.Administrator);

        adminGroup.MapGet("/", AdminListAsync);
        adminGroup.MapGet("/{id:guid}", AdminGetAsync);
        adminGroup.MapPost("/{id:guid}/status", AdminUpdateStatusAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateIncidentReportRequest request,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (request.BuildingId == Guid.Empty)
        {
            return ValidationProblem("buildingId", "buildingId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return ValidationProblem("content", "Report content is required.");
        }

        if (request.Content.Trim().Length > IncidentReport.MaxContentLength)
        {
            return ValidationProblem(
                "content", $"Report content must be at most {IncidentReport.MaxContentLength} characters.");
        }

        var mediaAttachmentIds = request.MediaAttachmentIds?.Distinct().ToList() ?? [];

        if (mediaAttachmentIds.Count > IncidentReport.MaxAttachments)
        {
            return ValidationProblem(
                "mediaAttachmentIds", $"A report may not have more than {IncidentReport.MaxAttachments} attachments.");
        }

        if (!await membershipAuthorizer.HasAccessAsync(principal, request.BuildingId, cancellationToken))
        {
            return Results.Problem(
                title: "You do not have access to this building.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.ReservationId is { } reservationId)
        {
            var reservationBuildingId = await dbContext.Reservations
                .AsNoTracking()
                .Where(reservation => reservation.Id == reservationId)
                .Select(reservation => (Guid?)reservation.BuildingId)
                .SingleOrDefaultAsync(cancellationToken);

            if (reservationBuildingId is null)
            {
                return ValidationProblem("reservationId", "Reservation not found.");
            }

            if (reservationBuildingId != request.BuildingId)
            {
                return ValidationProblem(
                    "reservationId", "The reservation does not belong to the specified building.");
            }
        }

        if (mediaAttachmentIds.Count > 0)
        {
            // Only attachments the caller themselves uploaded can be linked
            // — never someone else's file, regardless of role.
            var ownedCount = await dbContext.MediaAttachments
                .AsNoTracking()
                .CountAsync(
                    media => mediaAttachmentIds.Contains(media.Id) && media.UploadedByUserId == userId,
                    cancellationToken);

            if (ownedCount != mediaAttachmentIds.Count)
            {
                return ValidationProblem(
                    "mediaAttachmentIds",
                    "One or more attachments were not found or were not uploaded by you.");
            }

            var alreadyLinkedCount = await dbContext.IncidentReportAttachments
                .AsNoTracking()
                .CountAsync(link => mediaAttachmentIds.Contains(link.MediaAttachmentId), cancellationToken);

            if (alreadyLinkedCount > 0)
            {
                return ValidationProblem(
                    "mediaAttachmentIds", "One or more attachments are already linked to another report.");
            }
        }

        var report = new IncidentReport(
            Guid.NewGuid(),
            request.BuildingId,
            userId,
            request.ReservationId,
            request.Content,
            timeProvider.GetUtcNow());

        foreach (var mediaAttachmentId in mediaAttachmentIds)
        {
            report.AddAttachment(Guid.NewGuid(), mediaAttachmentId);
        }

        dbContext.IncidentReports.Add(report);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/reports/{report.Id}", ToResponse(report));
    }

    private static async Task<IResult> ListMineAsync(
        Guid buildingId,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (buildingId == Guid.Empty)
        {
            return ValidationProblem("buildingId", "buildingId is required.");
        }

        var reports = await dbContext.IncidentReports
            .AsNoTracking()
            .Include(report => report.Attachments)
            .Where(report => report.BuildingId == buildingId && report.ReportedByUserId == userId)
            .OrderByDescending(report => report.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(reports.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetMineAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        var report = await LoadReportAsync(dbContext, id, cancellationToken);

        if (report is null)
        {
            return Results.NotFound();
        }

        if (report.ReportedByUserId != userId)
        {
            return Results.Problem(
                title: "You do not have access to this report.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(ToResponse(report));
    }

    private static async Task<IResult> AdminListAsync(
        Guid buildingId,
        string? status,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (buildingId == Guid.Empty)
        {
            return ValidationProblem("buildingId", "buildingId is required.");
        }

        var query = dbContext.IncidentReports
            .AsNoTracking()
            .Include(report => report.Attachments)
            .Where(report => report.BuildingId == buildingId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<IncidentReportStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                return ValidationProblem("status", "Unknown status.");
            }

            query = query.Where(report => report.Status == parsedStatus);
        }

        var reports = await query
            .OrderByDescending(report => report.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(reports.Select(ToResponse).ToList());
    }

    private static async Task<IResult> AdminGetAsync(
        Guid id,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var report = await LoadReportAsync(dbContext, id, cancellationToken);

        return report is null ? Results.NotFound() : Results.Ok(ToResponse(report));
    }

    private static async Task<IResult> AdminUpdateStatusAsync(
        Guid id,
        UpdateIncidentReportStatusRequest request,
        ClaimsPrincipal principal,
        AppDbContext dbContext,
        IAuditRecorder auditRecorder,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } actorUserId)
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<IncidentReportStatus>(request.Status, ignoreCase: true, out var newStatus))
        {
            return ValidationProblem("status", "Unknown status.");
        }

        var report = await dbContext.IncidentReports
            .Include(candidate => candidate.Attachments)
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (report is null)
        {
            return Results.NotFound();
        }

        var previousStatus = report.Status;

        if (report.UpdateStatus(newStatus))
        {
            auditRecorder.Record(AuditRecord.ByUser(
                actorUserId,
                AuditAction.IncidentReportStatusChanged,
                AuditTargetType.IncidentReport,
                report.Id,
                report.BuildingId,
                AuditMetadata.IncidentReportStatusChanged(previousStatus.ToString(), newStatus.ToString())));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(ToResponse(report));
    }

    private static Task<IncidentReport?> LoadReportAsync(
        AppDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        dbContext.IncidentReports
            .AsNoTracking()
            .Include(report => report.Attachments)
            .SingleOrDefaultAsync(report => report.Id == id, cancellationToken);

    private static IncidentReportResponse ToResponse(IncidentReport report) =>
        new(
            report.Id,
            report.BuildingId,
            report.ReportedByUserId,
            report.ReservationId,
            report.Content,
            report.Status.ToString(),
            report.CreatedAtUtc,
            report.Attachments.Select(attachment => attachment.MediaAttachmentId).ToList());

    private static IResult ValidationProblem(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [key] = [message]
        });

    private sealed record CreateIncidentReportRequest(
        Guid BuildingId,
        Guid? ReservationId,
        string Content,
        List<Guid>? MediaAttachmentIds);

    private sealed record UpdateIncidentReportStatusRequest(string Status);

    private sealed record IncidentReportResponse(
        Guid Id,
        Guid BuildingId,
        Guid ReportedByUserId,
        Guid? ReservationId,
        string Content,
        string Status,
        DateTimeOffset CreatedAtUtc,
        List<Guid> MediaAttachmentIds);
}
