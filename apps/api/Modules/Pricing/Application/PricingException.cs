namespace ResidentialAmenities.Api.Modules.Pricing.Application;

/// <summary>
/// Thrown when a quote cannot be calculated authoritatively — e.g. no
/// active price rule for a requested amenity/use-type, or a currency
/// mismatch between components. Callers surface this as a client error
/// rather than a server fault.
/// </summary>
public sealed class PricingException(string message) : Exception(message);
