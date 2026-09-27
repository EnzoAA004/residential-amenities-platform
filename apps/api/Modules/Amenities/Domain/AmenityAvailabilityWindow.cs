namespace ResidentialAmenities.Api.Modules.Amenities.Domain;

/// <summary>
/// A recurring weekly operating window during which an amenity can be used.
/// Exact shift boundaries are business decisions pending validation
/// (issue #2); this type only stores whatever window configuration is set.
/// </summary>
public sealed class AmenityAvailabilityWindow
{
    private AmenityAvailabilityWindow()
    {
    }

    public AmenityAvailabilityWindow(
        Guid id,
        Guid amenityId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Window id is required.", nameof(id));
        }

        if (amenityId == Guid.Empty)
        {
            throw new ArgumentException(
                "Amenity id is required.",
                nameof(amenityId));
        }

        if (endTime <= startTime)
        {
            throw new ArgumentException(
                "End time must be after start time. Overnight windows are " +
                "not supported yet.",
                nameof(endTime));
        }

        Id = id;
        AmenityId = amenityId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
    }

    public Guid Id { get; private set; }

    public Guid AmenityId { get; private set; }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public Amenity Amenity { get; private set; } = null!;
}
