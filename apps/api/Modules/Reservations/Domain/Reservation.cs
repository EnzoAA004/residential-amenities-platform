using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Domain;

/// <summary>
/// The booking aggregate root (RF-004, RF-005). Building-scoped, attributed
/// to the resident membership that created it (never a client-sent user or
/// membership id — that is derived server-side by the endpoint). Uses
/// Pricing's <see cref="ReservationUseType"/> directly: Reservations already
/// depends on Pricing per module-boundaries.md, and both modules need the
/// exact same use-type value to select/apply the right price rule, so a
/// second, parallel enum would only invite drift.
/// </summary>
public sealed class Reservation
{
    private readonly List<ReservationResource> _resources = [];
    private readonly List<ReservationPriceLine> _priceLines = [];

    private Reservation()
    {
    }

    public Reservation(
        Guid id,
        Guid buildingId,
        Guid createdByMembershipId,
        ReservationUseType useType,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation id is required.",
                nameof(id));
        }

        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException(
                "Building id is required.",
                nameof(buildingId));
        }

        if (createdByMembershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Creating membership id is required.",
                nameof(createdByMembershipId));
        }

        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException(
                "End must be after start.",
                nameof(endsAtUtc));
        }

        Id = id;
        BuildingId = buildingId;
        CreatedByMembershipId = createdByMembershipId;
        UseType = useType;
        Status = ReservationStatus.Confirmed;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid BuildingId { get; private set; }

    public Guid CreatedByMembershipId { get; private set; }

    public ReservationUseType UseType { get; private set; }

    public ReservationStatus Status { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public DateTimeOffset EndsAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public IReadOnlyCollection<ReservationResource> Resources => _resources;

    public IReadOnlyCollection<ReservationPriceLine> PriceLines => _priceLines;

    public ReservationResource AddResource(Guid id, Guid amenityId, bool isExclusive)
    {
        var resource = new ReservationResource(id, Id, amenityId, isExclusive);
        _resources.Add(resource);
        return resource;
    }

    public ReservationPriceLine AddPriceLine(
        Guid id,
        Guid priceRuleId,
        Guid amenityId,
        PriceComponentType componentType,
        string currency,
        decimal amount,
        DateTimeOffset quotedAtUtc)
    {
        var line = new ReservationPriceLine(
            id,
            Id,
            priceRuleId,
            amenityId,
            componentType,
            currency,
            amount,
            quotedAtUtc);

        _priceLines.Add(line);
        return line;
    }

    public void Cancel(DateTimeOffset cancelledAtUtc)
    {
        if (Status == ReservationStatus.Cancelled)
        {
            return;
        }

        Status = ReservationStatus.Cancelled;
        CancelledAtUtc = cancelledAtUtc;
    }
}
