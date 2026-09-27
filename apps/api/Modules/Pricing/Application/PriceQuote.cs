namespace ResidentialAmenities.Api.Modules.Pricing.Application;

public sealed record PriceQuote(
    string Currency,
    decimal TotalAmount,
    DateTimeOffset QuotedAtUtc,
    IReadOnlyList<PriceQuoteLine> Lines);
