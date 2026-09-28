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
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
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

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException(
                "Expiration must be after creation.",
                nameof(expiresAtUtc));
        }

        Id = id;
        BuildingId = buildingId;
        CreatedByMembershipId = createdByMembershipId;
        UseType = useType;
        // Every reservation starts as a payment hold (RB-009): there is no
        // payment step yet to confirm it immediately. See ReservationStatus.
        Status = ReservationStatus.Pending;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
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

    /// <summary>
    /// When this hold stops blocking resources if it never becomes
    /// <see cref="ReservationStatus.Confirmed"/>. Conflict detection treats
    /// a <see cref="ReservationStatus.Pending"/> reservation as active only
    /// while <c>now &lt; ExpiresAtUtc</c>, independent of whether the
    /// expiration job has already flipped its <see cref="Status"/> — so a
    /// hold never blocks past its own deadline even if that job hasn't run
    /// yet.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ExpiredAtUtc { get; private set; }

    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

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

    /// <summary>
    /// Trusted-payment transition <see cref="ReservationStatus.Pending"/> →
    /// <see cref="ReservationStatus.Confirmed"/> (RB-009, RB-011). Returns
    /// whether the reservation is <see cref="ReservationStatus.Confirmed"/>
    /// after the call.
    ///
    /// Idempotent (<c>Confirmed → Confirmed</c> is a safe no-op) and never
    /// revives anything: an <see cref="ReservationStatus.Expired"/> or
    /// <see cref="ReservationStatus.Cancelled"/> reservation stays as it is,
    /// and so does a <see cref="ReservationStatus.Pending"/> hold that is
    /// already past <see cref="ExpiresAtUtc"/> — its resources may already
    /// have been booked by someone else, because conflict detection stops
    /// counting a hold the instant it is past due.
    /// </summary>
    public bool Confirm(DateTimeOffset nowUtc)
    {
        if (Status == ReservationStatus.Confirmed)
        {
            return true;
        }

        if (Status != ReservationStatus.Pending || nowUtc >= ExpiresAtUtc)
        {
            return false;
        }

        Status = ReservationStatus.Confirmed;
        ConfirmedAtUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// Transitions an unpaid hold to <see cref="ReservationStatus.Expired"/>
    /// (RF-012, RB-010). A no-op guard, not an exception, for anything that
    /// is not currently an active, past-due <see cref="ReservationStatus.Pending"/>
    /// hold — this method must never "revive" a reservation or touch
    /// <see cref="ReservationStatus.Confirmed"/>/<see cref="ReservationStatus.Cancelled"/>
    /// state, and calling it twice (or from two concurrent workers) is safe.
    /// </summary>
    public void Expire(DateTimeOffset nowUtc)
    {
        if (Status != ReservationStatus.Pending || nowUtc < ExpiresAtUtc)
        {
            return;
        }

        Status = ReservationStatus.Expired;
        ExpiredAtUtc = nowUtc;
    }
}
