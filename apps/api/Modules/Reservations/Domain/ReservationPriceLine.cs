using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Domain;

/// <summary>
/// A historical copy of one <c>PriceQuoteLine</c> (Pricing module),
/// snapshotted at reservation-creation time (RB-008, RF-009). It stores
/// <see cref="PriceRuleId"/> and <see cref="AmenityId"/> as plain ids for
/// traceability only — deliberately no foreign key to PriceRules, so this
/// history remains valid even if that rule is later changed or removed.
/// </summary>
public sealed class ReservationPriceLine
{
    private ReservationPriceLine()
    {
    }

    public ReservationPriceLine(
        Guid id,
        Guid reservationId,
        Guid priceRuleId,
        Guid amenityId,
        PriceComponentType componentType,
        string currency,
        decimal amount,
        DateTimeOffset quotedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Price line id is required.",
                nameof(id));
        }

        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation id is required.",
                nameof(reservationId));
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Amount must not be negative. Zero is allowed for a free " +
                "reservation type (e.g. Leisure, per DEC-014/RB-018).");
        }

        Id = id;
        ReservationId = reservationId;
        PriceRuleId = priceRuleId;
        AmenityId = amenityId;
        ComponentType = componentType;
        Currency = currency;
        Amount = amount;
        QuotedAtUtc = quotedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ReservationId { get; private set; }

    public Guid PriceRuleId { get; private set; }

    public Guid AmenityId { get; private set; }

    public PriceComponentType ComponentType { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public DateTimeOffset QuotedAtUtc { get; private set; }

    public Reservation Reservation { get; private set; } = null!;
}
