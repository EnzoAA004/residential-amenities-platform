using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Modules.Pricing.Application;

/// <summary>
/// One priced component of a quote, carrying enough information (rule id,
/// amount, currency) that a future reservation can copy it verbatim as a
/// historical snapshot line (RB-008) without re-reading PriceRule later.
/// </summary>
public sealed record PriceQuoteLine(
    Guid PriceRuleId,
    Guid AmenityId,
    PriceComponentType ComponentType,
    string Currency,
    decimal Amount);
