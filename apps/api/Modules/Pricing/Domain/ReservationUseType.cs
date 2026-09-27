namespace ResidentialAmenities.Api.Modules.Pricing.Domain;

/// <summary>
/// The reservation context a price rule applies to. Mirrors the use types
/// named in RF-004/RF-005/RF-006; Reservations (#20/#21) is the eventual
/// owner of the authoritative reservation-type concept once it exists.
/// </summary>
public enum ReservationUseType
{
    SharedLeisure = 0,
    ExclusiveLeisure = 1,
    Event = 2
}
