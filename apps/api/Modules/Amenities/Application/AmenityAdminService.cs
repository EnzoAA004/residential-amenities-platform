using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Infrastructure.Persistence;

namespace ResidentialAmenities.Api.Modules.Amenities.Application;

/// <summary>
/// Availability administration. Each change runs in a transaction holding the
/// same per-Amenity advisory lock reservation creation and reschedule use, so a
/// configuration change is serialized with bookings on that amenity, and it
/// commits together with its audit entry. Exact operating windows stay
/// business-configurable (issue #2); nothing here fixes them.
/// </summary>
public sealed class AmenityAdminService(
    AppDbContext dbContext,
    IAuditRecorder auditRecorder) : IAmenityAdminContract
{
    public const int MaxWindows = 28;

    public async Task<AmenitySummary?> GetSummaryAsync(
        Guid amenityId,
        CancellationToken cancellationToken) =>
        await dbContext.Amenities
            .AsNoTracking()
            .Where(amenity => amenity.Id == amenityId)
            .Select(amenity => new AmenitySummary(
                amenity.Id,
                amenity.BuildingId,
                amenity.Name,
                amenity.Kind,
                amenity.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<AdminAvailabilityConfig> GetAvailabilityAsync(
        Guid buildingId,
        Guid amenityId,
        CancellationToken cancellationToken)
    {
        var amenity = await dbContext.Amenities
            .AsNoTracking()
            .Include(candidate => candidate.AvailabilityWindows)
            .Include(candidate => candidate.UnavailablePeriods)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == amenityId && candidate.BuildingId == buildingId,
                cancellationToken)
            ?? throw NotFound();

        return new AdminAvailabilityConfig(
            amenity.Id,
            amenity.BuildingId,
            amenity.AvailabilityWindows
                .OrderBy(window => window.DayOfWeek)
                .ThenBy(window => window.StartTime)
                .Select(window => new AdminAvailabilityWindow(
                    window.Id, window.DayOfWeek, window.StartTime, window.EndTime))
                .ToList(),
            amenity.UnavailablePeriods
                .OrderBy(period => period.StartsAtUtc)
                .Select(period => new AdminUnavailablePeriod(
                    period.Id, period.StartsAtUtc, period.EndsAtUtc, period.Reason))
                .ToList());
    }

    public async Task ReplaceAvailabilityAsync(
        Guid buildingId,
        Guid amenityId,
        IReadOnlyList<AvailabilityWindowInput> windows,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateWindows(windows);

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await ResourceAdvisoryLock.AcquireAsync(dbContext, [amenityId], cancellationToken);

        var amenity = await LoadAsync(buildingId, amenityId, cancellationToken);

        dbContext.AmenityAvailabilityWindows.RemoveRange(amenity.AvailabilityWindows.ToList());

        // Explicit Add: a child with a preset key on a loaded aggregate would
        // otherwise be treated as an existing row (UPDATE) by EF.
        foreach (var window in windows)
        {
            dbContext.AmenityAvailabilityWindows.Add(amenity.AddAvailabilityWindow(
                Guid.NewGuid(), window.DayOfWeek, window.StartTime, window.EndTime));
        }

        auditRecorder.Record(AuditRecord.ByUser(
            actorUserId,
            AuditAction.AmenityAvailabilityChanged,
            AuditTargetType.Amenity,
            amenity.Id,
            amenity.BuildingId,
            AuditMetadata.AmenityAvailability("WindowsReplaced", windows.Count)));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid> AddUnavailablePeriodAsync(
        Guid buildingId,
        Guid amenityId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        string? reason,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        startsAtUtc = UtcInstant.Normalize(startsAtUtc);
        endsAtUtc = UtcInstant.Normalize(endsAtUtc);

        if (endsAtUtc <= startsAtUtc)
        {
            throw new AmenityRequestException("End must be after start.", StatusCodes.Status400BadRequest);
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        if (trimmedReason is { Length: > AuditMetadata.MaxReasonLength })
        {
            throw new AmenityRequestException(
                $"The reason cannot exceed {AuditMetadata.MaxReasonLength} characters.",
                StatusCodes.Status400BadRequest);
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await ResourceAdvisoryLock.AcquireAsync(dbContext, [amenityId], cancellationToken);

        var amenity = await LoadAsync(buildingId, amenityId, cancellationToken);

        if (amenity.UnavailablePeriods.Any(existing =>
                existing.StartsAtUtc == startsAtUtc && existing.EndsAtUtc == endsAtUtc))
        {
            throw new AmenityRequestException(
                "An identical unavailable period already exists.",
                StatusCodes.Status409Conflict);
        }

        var period = amenity.AddUnavailablePeriod(Guid.NewGuid(), startsAtUtc, endsAtUtc, trimmedReason);
        dbContext.AmenityUnavailablePeriods.Add(period);

        auditRecorder.Record(AuditRecord.ByUser(
            actorUserId,
            AuditAction.AmenityAvailabilityChanged,
            AuditTargetType.Amenity,
            amenity.Id,
            amenity.BuildingId,
            AuditMetadata.AmenityAvailability("UnavailablePeriodAdded", 1, trimmedReason)));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return period.Id;
    }

    public async Task RemoveUnavailablePeriodAsync(
        Guid buildingId,
        Guid amenityId,
        Guid periodId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await ResourceAdvisoryLock.AcquireAsync(dbContext, [amenityId], cancellationToken);

        var amenity = await LoadAsync(buildingId, amenityId, cancellationToken);

        var period = amenity.UnavailablePeriods.SingleOrDefault(candidate => candidate.Id == periodId)
            ?? throw new AmenityRequestException(
                "Unavailable period not found.",
                StatusCodes.Status404NotFound);

        dbContext.AmenityUnavailablePeriods.Remove(period);

        auditRecorder.Record(AuditRecord.ByUser(
            actorUserId,
            AuditAction.AmenityAvailabilityChanged,
            AuditTargetType.Amenity,
            amenity.Id,
            amenity.BuildingId,
            AuditMetadata.AmenityAvailability("UnavailablePeriodRemoved", 1)));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Amenity> LoadAsync(
        Guid buildingId,
        Guid amenityId,
        CancellationToken cancellationToken) =>
        await dbContext.Amenities
            .Include(candidate => candidate.AvailabilityWindows)
            .Include(candidate => candidate.UnavailablePeriods)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == amenityId && candidate.BuildingId == buildingId,
                cancellationToken)
        ?? throw NotFound();

    private static AmenityRequestException NotFound() =>
        new("Amenity not found for this building.", StatusCodes.Status404NotFound);

    private static void ValidateWindows(IReadOnlyList<AvailabilityWindowInput> windows)
    {
        if (windows.Count == 0 || windows.Count > MaxWindows)
        {
            throw new AmenityRequestException(
                $"Between 1 and {MaxWindows} availability windows are required. " +
                "To close an amenity use an unavailable period.",
                StatusCodes.Status400BadRequest);
        }

        foreach (var window in windows)
        {
            if (!Enum.IsDefined(window.DayOfWeek))
            {
                throw new AmenityRequestException("Unknown day of week.", StatusCodes.Status400BadRequest);
            }

            if (window.EndTime <= window.StartTime)
            {
                throw new AmenityRequestException(
                    "Each window must end after it starts (overnight windows are not supported yet).",
                    StatusCodes.Status400BadRequest);
            }
        }

        foreach (var day in windows.GroupBy(window => window.DayOfWeek))
        {
            var ordered = day.OrderBy(window => window.StartTime).ToList();

            for (var index = 1; index < ordered.Count; index++)
            {
                if (ordered[index].StartTime < ordered[index - 1].EndTime)
                {
                    throw new AmenityRequestException(
                        $"Overlapping or duplicate windows on {day.Key}.",
                        StatusCodes.Status422UnprocessableEntity);
                }
            }
        }
    }
}
