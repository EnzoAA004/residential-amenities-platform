namespace ResidentialAmenities.Api.Modules.Buildings.Domain;

public sealed class Unit
{
    private Unit()
    {
    }

    public Unit(
        Guid id,
        Guid buildingId,
        int floor,
        string door,
        string label)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Unit id is required.", nameof(id));
        }

        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException(
                "Building id is required.",
                nameof(buildingId));
        }

        Id = id;
        BuildingId = buildingId;
        Floor = floor;
        Door = RequireText(door, nameof(door)).ToUpperInvariant();
        Label = RequireText(label, nameof(label)).ToUpperInvariant();
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid BuildingId { get; private set; }

    public int Floor { get; private set; }

    public string Door { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public Building Building { get; private set; } = null!;

    public ICollection<ResidentMembership> Memberships { get; } =
        new List<ResidentMembership>();

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }
}
