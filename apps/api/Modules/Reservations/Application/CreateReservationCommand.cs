using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Server-validated intent to create a reservation. <see cref="MembershipId"/>
/// is resolved by the endpoint from the authenticated caller — never taken
/// from client input.
/// </summary>
public sealed record CreateReservationCommand(
    Guid BuildingId,
    Guid AmenityId,
    ReservationUseType UseType,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    Guid MembershipId);
