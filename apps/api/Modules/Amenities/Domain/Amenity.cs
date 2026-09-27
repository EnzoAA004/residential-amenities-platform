namespace ResidentialAmenities.Api.Modules.Amenities.Domain;

public sealed class Amenity
{
    private readonly List<AmenityAvailabilityWindow> _availabilityWindows = [];
    private readonly List<AmenityUnavailablePeriod> _unavailablePeriods = [];

    private Amenity()
    {
    }

    public Amenity(
        Guid id,
        Guid buildingId,
        string name,
        AmenityKind kind,
        bool allowsSharedUse,
        bool allowsExclusiveUse)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Amenity id is required.", nameof(id));
        }

        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException(
                "Building id is required.",
                nameof(buildingId));
        }

        if (!allowsSharedUse && !allowsExclusiveUse)
        {
            throw new ArgumentException(
                "An amenity must allow shared use, exclusive use, or both.");
        }

        Id = id;
        BuildingId = buildingId;
        Name = RequireText(name, nameof(name));
        Kind = kind;
        AllowsSharedUse = allowsSharedUse;
        AllowsExclusiveUse = allowsExclusiveUse;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid BuildingId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public AmenityKind Kind { get; private set; }

    public bool AllowsSharedUse { get; private set; }

    public bool AllowsExclusiveUse { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<AmenityAvailabilityWindow> AvailabilityWindows =>
        _availabilityWindows;

    public IReadOnlyCollection<AmenityUnavailablePeriod> UnavailablePeriods =>
        _unavailablePeriods;

    public AmenityAvailabilityWindow AddAvailabilityWindow(
        Guid id,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        var window = new AmenityAvailabilityWindow(
            id,
            Id,
            dayOfWeek,
            startTime,
            endTime);

        _availabilityWindows.Add(window);
        return window;
    }

    public AmenityUnavailablePeriod AddUnavailablePeriod(
        Guid id,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        string? reason)
    {
        var period = new AmenityUnavailablePeriod(
            id,
            Id,
            startsAtUtc,
            endsAtUtc,
            reason);

        _unavailablePeriods.Add(period);
        return period;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }
}
