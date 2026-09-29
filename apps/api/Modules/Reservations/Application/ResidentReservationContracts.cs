namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// The resident-facing "my reservations" read side (issue #66). Deliberately
/// narrower than <see cref="AdminReservationRow"/>: it never carries
/// <c>CreatedByMembershipId</c> or any other actor id — the caller already
/// knows these are their own reservations (ownership was resolved
/// server-side, never accepted as a query parameter).
/// </summary>
public sealed record ResidentReservationResource(Guid AmenityId, bool IsExclusive);

public sealed record ResidentReservationRow(
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
    IReadOnlyList<ResidentReservationResource> Resources,
    string? Currency,
    decimal Total);

public sealed record ResidentReservationPage(
    IReadOnlyList<ResidentReservationRow> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// Read side of "my reservations". Every result is already scoped to one
/// resident membership — this is not a general building listing.
/// </summary>
public interface IResidentReservationQuery
{
    Task<ResidentReservationPage> ListAsync(
        Guid buildingId,
        Guid createdByMembershipId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
