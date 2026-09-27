namespace ResidentialAmenities.Api.Modules.Buildings.Domain;

public sealed class Building
{
    private Building()
    {
    }

    public Building(
        Guid id,
        string name,
        string timeZoneId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Building id is required.", nameof(id));
        }

        Id = id;
        Name = RequireText(name, nameof(name));
        TimeZoneId = RequireText(timeZoneId, nameof(timeZoneId));
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string TimeZoneId { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public ICollection<Unit> Units { get; } = new List<Unit>();

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }
}
