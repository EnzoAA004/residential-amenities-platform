namespace ResidentialAmenities.Api.Modules.Pricing.Domain;

/// <summary>
/// A configured, effective-dated price for one amenity/component/use-type
/// combination. Pricing is configuration data (RB-007): nothing here is a
/// business constant baked into code, and existing reservation snapshots
/// must never change when a rule is superseded (RB-008) — callers achieve
/// that by copying the calculated amount, not by re-referencing this row.
/// </summary>
public sealed class PriceRule
{
    private PriceRule()
    {
    }

    public PriceRule(
        Guid id,
        Guid buildingId,
        Guid amenityId,
        PriceComponentType componentType,
        ReservationUseType useType,
        string currency,
        decimal amount,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Price rule id is required.", nameof(id));
        }

        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException(
                "Building id is required.",
                nameof(buildingId));
        }

        if (amenityId == Guid.Empty)
        {
            throw new ArgumentException(
                "Amenity id is required.",
                nameof(amenityId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Amount must be positive.");
        }

        if (effectiveToUtc is { } endUtc && endUtc <= effectiveFromUtc)
        {
            throw new ArgumentException(
                "Effective end must be after effective start.",
                nameof(effectiveToUtc));
        }

        Id = id;
        BuildingId = buildingId;
        AmenityId = amenityId;
        ComponentType = componentType;
        UseType = useType;
        Currency = RequireCurrency(currency);
        Amount = amount;
        EffectiveFromUtc = effectiveFromUtc;
        EffectiveToUtc = effectiveToUtc;
    }

    public Guid Id { get; private set; }

    public Guid BuildingId { get; private set; }

    public Guid AmenityId { get; private set; }

    public PriceComponentType ComponentType { get; private set; }

    public ReservationUseType UseType { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public DateTimeOffset EffectiveFromUtc { get; private set; }

    public DateTimeOffset? EffectiveToUtc { get; private set; }

    public bool IsEffectiveAt(DateTimeOffset atUtc) =>
        EffectiveFromUtc <= atUtc &&
        (EffectiveToUtc is null || EffectiveToUtc > atUtc);

    /// <summary>
    /// Closes this rule's effective period, e.g. because a replacement rule
    /// takes over. This never touches amounts already snapshotted by
    /// reservations quoted before <paramref name="endUtc"/>.
    /// </summary>
    public void Supersede(DateTimeOffset endUtc)
    {
        if (endUtc <= EffectiveFromUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endUtc),
                "End date cannot precede or equal the effective start.");
        }

        EffectiveToUtc = endUtc;
    }

    private static string RequireCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException(
                "Currency must be a 3-letter ISO code.",
                nameof(currency));
        }

        return currency.Trim().ToUpperInvariant();
    }
}
