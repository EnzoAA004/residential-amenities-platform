using ResidentialAmenities.Api.Modules.Amenities.Domain;

namespace ResidentialAmenities.Api.Modules.Amenities.Application;

public sealed class AmenityRequestException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record AmenitySummary(
    Guid Id,
    Guid BuildingId,
    string Name,
    AmenityKind Kind,
    bool IsActive);

public sealed record AvailabilityWindowInput(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

public sealed record AdminAvailabilityWindow(Guid Id, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

public sealed record AdminUnavailablePeriod(
    Guid Id,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string? Reason);

public sealed record AdminAvailabilityConfig(
    Guid AmenityId,
    Guid BuildingId,
    IReadOnlyList<AdminAvailabilityWindow> Windows,
    IReadOnlyList<AdminUnavailablePeriod> UnavailablePeriods);

/// <summary>
/// Availability administration owned by Amenities. A change only shapes future
/// availability: existing reservations are never modified or cancelled.
/// </summary>
public interface IAmenityAdminContract
{
    /// <summary>Lightweight lookup other modules use to validate an amenity/building pair.</summary>
    Task<AmenitySummary?> GetSummaryAsync(Guid amenityId, CancellationToken cancellationToken);

    Task<AdminAvailabilityConfig> GetAvailabilityAsync(
        Guid buildingId,
        Guid amenityId,
        CancellationToken cancellationToken);

    Task ReplaceAvailabilityAsync(
        Guid buildingId,
        Guid amenityId,
        IReadOnlyList<AvailabilityWindowInput> windows,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<Guid> AddUnavailablePeriodAsync(
        Guid buildingId,
        Guid amenityId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        string? reason,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task RemoveUnavailablePeriodAsync(
        Guid buildingId,
        Guid amenityId,
        Guid periodId,
        Guid actorUserId,
        CancellationToken cancellationToken);
}
