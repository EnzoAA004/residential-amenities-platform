namespace ResidentialAmenities.Api.Modules.Amenities.Domain;

/// <summary>
/// A one-off blackout (maintenance, closure, etc.) that overrides the
/// recurring availability windows for a bounded UTC time range.
/// </summary>
public sealed class AmenityUnavailablePeriod
{
    private AmenityUnavailablePeriod()
    {
    }

    public AmenityUnavailablePeriod(
        Guid id,
        Guid amenityId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        string? reason)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Period id is required.", nameof(id));
        }

        if (amenityId == Guid.Empty)
        {
            throw new ArgumentException(
                "Amenity id is required.",
                nameof(amenityId));
        }

        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException(
                "End must be after start.",
                nameof(endsAtUtc));
        }

        Id = id;
        AmenityId = amenityId;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public Guid Id { get; private set; }

    public Guid AmenityId { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public DateTimeOffset EndsAtUtc { get; private set; }

    public string? Reason { get; private set; }

    public Amenity Amenity { get; private set; } = null!;
}
