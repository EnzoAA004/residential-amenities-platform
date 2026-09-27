namespace ResidentialAmenities.Api.Modules.Pricing.Domain;

/// <summary>
/// Whether a price rule prices the base reservable resource (e.g. the SUM
/// itself) or an optional add-on attached to it (e.g. pool, barbecue), per
/// RB-002.
/// </summary>
public enum PriceComponentType
{
    Base = 0,
    AddOn = 1
}
