namespace ResidentialAmenities.Api.Modules.Reservations.Domain;

/// <summary>
/// A configured, bookable time-of-day window for Event reservations (issue
/// #21) — e.g. an "afternoon" or "evening" shift. This is deliberately
/// separate from <c>AmenityAvailabilityWindow</c> (Amenities module): that
/// type expresses when a resource is physically/operationally usable at
/// all, while this expresses the commercial policy of which windows within
/// that availability may be booked as an Event. Conflating the two would
/// force Amenities to encode business/reservation policy it does not own.
///
/// Exact slot names/times are configuration, not code constants, and remain
/// explicitly TBD pending issue #2 (afternoon/night shift boundaries,
/// whether full-day is in MVP). Seeded values are placeholders only.
/// </summary>
public sealed class EventSlotDefinition
{
    private EventSlotDefinition()
    {
    }

    public EventSlotDefinition(
        Guid id,
        Guid buildingId,
        string name,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Event slot id is required.",
                nameof(id));
        }

        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException(
                "Building id is required.",
                nameof(buildingId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (endTime <= startTime)
        {
            throw new ArgumentException(
                "End time must be after start time. Overnight/full-day " +
                "slots are not supported yet.",
                nameof(endTime));
        }

        Id = id;
        BuildingId = buildingId;
        Name = name.Trim();
        StartTime = startTime;
        EndTime = endTime;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid BuildingId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public bool IsActive { get; private set; }

    public bool Matches(TimeOnly startTime, TimeOnly endTime) =>
        IsActive && StartTime == startTime && EndTime == endTime;

    /// <summary>
    /// Administrative change of the name and/or times. Same invariants as
    /// creation (no overnight). Existing reservations are unaffected.
    /// </summary>
    public void Update(string name, TimeOnly startTime, TimeOnly endTime)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (endTime <= startTime)
        {
            throw new ArgumentException(
                "End time must be after start time. Overnight/full-day slots are not supported yet.",
                nameof(endTime));
        }

        Name = name.Trim();
        StartTime = startTime;
        EndTime = endTime;
    }

    /// <returns>true when this call changed the state.</returns>
    public bool Deactivate()
    {
        var changed = IsActive;
        IsActive = false;
        return changed;
    }

    /// <returns>true when this call changed the state.</returns>
    public bool Activate()
    {
        var changed = !IsActive;
        IsActive = true;
        return changed;
    }
}
