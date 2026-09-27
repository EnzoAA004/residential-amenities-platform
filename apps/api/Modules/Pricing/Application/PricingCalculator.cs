using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Modules.Pricing.Application;

/// <summary>
/// Authoritative, server-side price calculation (RF-008, RB-007). Pure
/// function of already-loaded price rules: it has no database/HTTP
/// dependency so it can be unit-tested directly and reused unchanged once
/// Reservations (#20/#21/#23) calls it to build a historical snapshot
/// (RB-008, RF-009).
/// </summary>
public static class PricingCalculator
{
    public static PriceQuote Calculate(
        IReadOnlyCollection<PriceRule> candidateRules,
        Guid baseAmenityId,
        ReservationUseType useType,
        IReadOnlyCollection<Guid> addOnAmenityIds,
        DateTimeOffset atUtc)
    {
        var baseRule = SelectEffectiveRule(
            candidateRules,
            baseAmenityId,
            PriceComponentType.Base,
            useType,
            atUtc);

        var lines = new List<PriceQuoteLine> { ToLine(baseRule) };

        foreach (var addOnAmenityId in addOnAmenityIds.Distinct())
        {
            var addOnRule = SelectEffectiveRule(
                candidateRules,
                addOnAmenityId,
                PriceComponentType.AddOn,
                useType,
                atUtc);

            if (!string.Equals(
                    addOnRule.Currency,
                    baseRule.Currency,
                    StringComparison.Ordinal))
            {
                throw new PricingException(
                    $"Add-on amenity {addOnAmenityId} is priced in " +
                    $"{addOnRule.Currency}, which does not match the base " +
                    $"currency {baseRule.Currency}.");
            }

            lines.Add(ToLine(addOnRule));
        }

        return new PriceQuote(
            baseRule.Currency,
            lines.Sum(line => line.Amount),
            atUtc,
            lines);
    }

    private static PriceRule SelectEffectiveRule(
        IReadOnlyCollection<PriceRule> candidateRules,
        Guid amenityId,
        PriceComponentType componentType,
        ReservationUseType useType,
        DateTimeOffset atUtc)
    {
        var effectiveRules = candidateRules
            .Where(rule =>
                rule.AmenityId == amenityId &&
                rule.ComponentType == componentType &&
                rule.UseType == useType &&
                rule.IsEffectiveAt(atUtc))
            .OrderByDescending(rule => rule.EffectiveFromUtc)
            .ToList();

        if (effectiveRules.Count == 0)
        {
            throw new PricingException(
                $"No active {componentType} price rule for amenity " +
                $"{amenityId} ({useType}) at {atUtc:O}.");
        }

        return effectiveRules[0];
    }

    private static PriceQuoteLine ToLine(PriceRule rule) =>
        new(rule.Id, rule.AmenityId, rule.ComponentType, rule.Currency, rule.Amount);
}
