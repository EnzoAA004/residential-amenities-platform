using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Server-validated intent to create a reservation. <see cref="MembershipId"/>
/// is resolved by the endpoint from the authenticated caller — never taken
/// from client input. <see cref="AmenityId"/> is the base resource (the only
/// resource for Shared/Exclusive Leisure; the SUM for Event).
/// <see cref="AddOnAmenityIds"/> is only meaningful for
/// <see cref="ReservationUseType.Event"/> and must be empty otherwise.
/// </summary>
public sealed record CreateReservationCommand(
    Guid BuildingId,
    Guid AmenityId,
    IReadOnlyList<Guid> AddOnAmenityIds,
    ReservationUseType UseType,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    Guid MembershipId);
