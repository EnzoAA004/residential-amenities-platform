namespace ResidentialAmenities.Api.Modules.Reservations.Domain;

/// <summary>
/// One resource (Amenity, referenced by id only — see module-boundaries.md)
/// attached to a reservation. Compatibility/overlap rules are evaluated per
/// resource, not per reservation or per building, so an Event reservation
/// (#21) can later attach several resources with independent exclusivity.
/// </summary>
public sealed class ReservationResource
{
    private ReservationResource()
    {
    }

    public ReservationResource(Guid id, Guid reservationId, Guid amenityId, bool isExclusive)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation resource id is required.",
                nameof(id));
        }

        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation id is required.",
                nameof(reservationId));
        }

        if (amenityId == Guid.Empty)
        {
            throw new ArgumentException(
                "Amenity id is required.",
                nameof(amenityId));
        }

        Id = id;
        ReservationId = reservationId;
        AmenityId = amenityId;
        IsExclusive = isExclusive;
    }

    public Guid Id { get; private set; }

    public Guid ReservationId { get; private set; }

    public Guid AmenityId { get; private set; }

    public bool IsExclusive { get; private set; }

    public Reservation Reservation { get; private set; } = null!;
}
