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
        TimeOnly endTime,
        bool isOvernight = false)
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

        ValidateTimes(startTime, endTime, isOvernight);

        Id = id;
        BuildingId = buildingId;
        Name = name.Trim();
        StartTime = startTime;
        EndTime = endTime;
        IsOvernight = isOvernight;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid BuildingId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    /// <summary>
    /// When true, this slot deliberately crosses midnight (e.g. 20:00 →
    /// 03:00 the next day, DEC-014/OQ-002): <see cref="EndTime"/> is a
    /// time-of-day on the calendar day *after* <see cref="StartTime"/>, not
    /// an invalid same-day range. A slot only matches a request that spans
    /// exactly one midnight boundary when this is true (see
    /// <c>ReservationScheduleValidator.EnsureValidEventSlotAsync</c>) — an
    /// arbitrary, unconfigured end-before-start range is still rejected
    /// everywhere else.
    /// </summary>
    public bool IsOvernight { get; private set; }

    public bool IsActive { get; private set; }

    public bool Matches(TimeOnly startTime, TimeOnly endTime) =>
        IsActive && StartTime == startTime && EndTime == endTime;

    /// <summary>
    /// Administrative change of the name and/or times. Same invariants as
    /// creation. Existing reservations are unaffected.
    /// </summary>
    public void Update(string name, TimeOnly startTime, TimeOnly endTime, bool isOvernight)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        ValidateTimes(startTime, endTime, isOvernight);

        Name = name.Trim();
        StartTime = startTime;
        EndTime = endTime;
        IsOvernight = isOvernight;
    }

    private static void ValidateTimes(TimeOnly startTime, TimeOnly endTime, bool isOvernight)
    {
        if (isOvernight)
        {
            if (endTime >= startTime)
            {
                throw new ArgumentException(
                    "An overnight slot's end time must be before its " +
                    "start time (it crosses midnight into the next day).",
                    nameof(endTime));
            }
        }
        else if (endTime <= startTime)
        {
            throw new ArgumentException(
                "End time must be after start time. Pass isOvernight: " +
                "true for a slot that deliberately crosses midnight.",
                nameof(endTime));
        }
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
