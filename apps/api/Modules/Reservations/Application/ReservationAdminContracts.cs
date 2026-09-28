using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

public sealed record AdminPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record AdminReservationFilter(
    Guid? BuildingId,
    ReservationStatus? Status,
    ReservationUseType? UseType,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? MembershipId,
    int Page,
    int PageSize);

public sealed record AdminReservationResource(Guid AmenityId, bool IsExclusive);

public sealed record AdminReservationPriceLine(
    Guid AmenityId,
    string ComponentType,
    string Currency,
    decimal Amount);

/// <summary>The administrative view of a reservation's own state (payments are joined by Administration).</summary>
public sealed record AdminReservationRow(
    Guid ReservationId,
    Guid BuildingId,
    string UseType,
    string Status,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    DateTimeOffset? ExpiredAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? CancellationReason,
    Guid CreatedByMembershipId,
    IReadOnlyList<AdminReservationResource> Resources,
    decimal Total,
    string? Currency);

public sealed record AdminReservationDetail(
    AdminReservationRow Reservation,
    IReadOnlyList<AdminReservationPriceLine> PriceLines);

/// <summary>Read side of the administrative reservation use cases (no aggregates loaded, no tracking).</summary>
public interface IReservationAdminQuery
{
    Task<AdminPage<AdminReservationRow>> ListAsync(
        AdminReservationFilter query,
        CancellationToken cancellationToken);

    Task<AdminReservationDetail?> GetAsync(
        Guid reservationId,
        CancellationToken cancellationToken);
}

public enum ReservationCancelOutcome
{
    Cancelled = 0,

    /// <summary>Already cancelled: nothing changed, original timestamp/reason kept, nothing audited.</summary>
    AlreadyCancelled = 1,

    NotFound = 2,

    /// <summary>Expired reservations are not cancelled.</summary>
    NotCancellable = 3
}

public sealed record ReservationRescheduleResult(bool Changed);

/// <summary>
/// Administrative commands owned by Reservations. Administration only
/// orchestrates: every invariant (state, availability, conflicts, locking,
/// audit atomicity) is enforced here.
/// </summary>
public interface IReservationAdminContract
{
    /// <summary>
    /// Cancels a Pending or Confirmed reservation (mandatory reason, RB-014).
    /// Does not touch any payment and does not refund (OQ-011).
    /// </summary>
    Task<ReservationCancelOutcome> CancelAsync(
        Guid reservationId,
        string reason,
        Guid actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a Confirmed, or still-held Pending, reservation to another time
    /// range (mandatory reason). Resources, price snapshot and
    /// <c>ExpiresAtUtc</c> never change.
    /// </summary>
    Task<ReservationRescheduleResult> RescheduleAsync(
        Guid reservationId,
        DateTimeOffset newStartsAtUtc,
        DateTimeOffset newEndsAtUtc,
        string reason,
        Guid actorUserId,
        CancellationToken cancellationToken);
}

public sealed record AdminEventSlot(
    Guid Id,
    Guid BuildingId,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsActive);

/// <summary>Event slot configuration (owned by Reservations). Slots are deactivated, never deleted.</summary>
public interface IEventSlotAdminContract
{
    Task<IReadOnlyList<AdminEventSlot>> ListAsync(
        Guid buildingId,
        CancellationToken cancellationToken);

    Task<AdminEventSlot> CreateAsync(
        Guid buildingId,
        string name,
        TimeOnly startTime,
        TimeOnly endTime,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<AdminEventSlot> UpdateAsync(
        Guid slotId,
        string name,
        TimeOnly startTime,
        TimeOnly endTime,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<AdminEventSlot> DeactivateAsync(
        Guid slotId,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<AdminEventSlot> ActivateAsync(
        Guid slotId,
        Guid actorUserId,
        CancellationToken cancellationToken);
}
