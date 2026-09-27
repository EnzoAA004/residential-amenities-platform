using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Modules.Buildings.Domain;

public sealed class ResidentMembership
{
    private ResidentMembership()
    {
    }

    public ResidentMembership(
        Guid id,
        Guid buildingId,
        Guid unitId,
        Guid userId,
        DateTimeOffset startedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(id));
        }

        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException(
                "Building id is required.",
                nameof(buildingId));
        }

        if (unitId == Guid.Empty)
        {
            throw new ArgumentException("Unit id is required.", nameof(unitId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        Id = id;
        BuildingId = buildingId;
        UnitId = unitId;
        UserId = userId;
        StartedAtUtc = startedAtUtc;
        Status = ResidentMembershipStatus.Active;
    }

    public Guid Id { get; private set; }

    public Guid BuildingId { get; private set; }

    public Guid UnitId { get; private set; }

    public Guid UserId { get; private set; }

    public ResidentMembershipStatus Status { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    public Unit Unit { get; private set; } = null!;

    public UserAccount User { get; private set; } = null!;

    public void Deactivate(DateTimeOffset endedAtUtc)
    {
        if (endedAtUtc < StartedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endedAtUtc),
                "End date cannot precede start date.");
        }

        Status = ResidentMembershipStatus.Inactive;
        EndedAtUtc = endedAtUtc;
    }
}
