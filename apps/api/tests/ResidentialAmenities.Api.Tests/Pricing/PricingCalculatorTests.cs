using ResidentialAmenities.Api.Modules.Pricing.Application;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Pricing;

public sealed class PricingCalculatorTests
{
    private static readonly Guid SumId = Guid.NewGuid();
    private static readonly Guid PoolId = Guid.NewGuid();
    private static readonly Guid BuildingId = Guid.NewGuid();

    [Fact]
    public void BaseRuleOnly_ReturnsSingleLineTotal()
    {
        var rule = CreateRule(
            SumId,
            PriceComponentType.Base,
            ReservationUseType.SharedLeisure,
            "ARS",
            5_000m,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null);

        var quote = PricingCalculator.Calculate(
            [rule],
            SumId,
            ReservationUseType.SharedLeisure,
            [],
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(5_000m, quote.TotalAmount);
        Assert.Equal("ARS", quote.Currency);
        var line = Assert.Single(quote.Lines);
        Assert.Equal(rule.Id, line.PriceRuleId);
    }

    [Fact]
    public void BaseAndAddOn_SumsAmounts()
    {
        var baseRule = CreateRule(
            SumId,
            PriceComponentType.Base,
            ReservationUseType.Event,
            "ARS",
            15_000m,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null);

        var addOnRule = CreateRule(
            PoolId,
            PriceComponentType.AddOn,
            ReservationUseType.Event,
            "ARS",
            3_000m,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null);

        var quote = PricingCalculator.Calculate(
            [baseRule, addOnRule],
            SumId,
            ReservationUseType.Event,
            [PoolId],
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(18_000m, quote.TotalAmount);
        Assert.Equal(2, quote.Lines.Count);
    }

    [Fact]
    public void SupersededRule_DoesNotAffectHistoricalQuoteBeforeChange()
    {
        var oldRule = CreateRule(
            SumId,
            PriceComponentType.Base,
            ReservationUseType.SharedLeisure,
            "ARS",
            5_000m,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null);

        var supersedeAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        oldRule.Supersede(supersedeAt);

        var newRule = CreateRule(
            SumId,
            PriceComponentType.Base,
            ReservationUseType.SharedLeisure,
            "ARS",
            7_500m,
            supersedeAt,
            null);

        // A reservation quoted before the rule change still resolves to the
        // old amount (RB-008/RF-009): changing a rule must not rewrite
        // history.
        var historicalQuote = PricingCalculator.Calculate(
            [oldRule, newRule],
            SumId,
            ReservationUseType.SharedLeisure,
            [],
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var currentQuote = PricingCalculator.Calculate(
            [oldRule, newRule],
            SumId,
            ReservationUseType.SharedLeisure,
            [],
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(5_000m, historicalQuote.TotalAmount);
        Assert.Equal(7_500m, currentQuote.TotalAmount);
    }

    [Fact]
    public void NoActiveRule_ThrowsPricingException()
    {
        Assert.Throws<PricingException>(() =>
            PricingCalculator.Calculate(
                [],
                SumId,
                ReservationUseType.SharedLeisure,
                [],
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MismatchedAddOnCurrency_ThrowsPricingException()
    {
        var baseRule = CreateRule(
            SumId,
            PriceComponentType.Base,
            ReservationUseType.Event,
            "ARS",
            15_000m,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null);

        var addOnRule = CreateRule(
            PoolId,
            PriceComponentType.AddOn,
            ReservationUseType.Event,
            "USD",
            10m,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null);

        Assert.Throws<PricingException>(() =>
            PricingCalculator.Calculate(
                [baseRule, addOnRule],
                SumId,
                ReservationUseType.Event,
                [PoolId],
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    private static PriceRule CreateRule(
        Guid amenityId,
        PriceComponentType componentType,
        ReservationUseType useType,
        string currency,
        decimal amount,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc) =>
        new(
            Guid.NewGuid(),
            BuildingId,
            amenityId,
            componentType,
            useType,
            currency,
            amount,
            effectiveFromUtc,
            effectiveToUtc);
}
