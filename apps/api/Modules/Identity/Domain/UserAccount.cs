using ResidentialAmenities.Api.Modules.Buildings.Domain;

namespace ResidentialAmenities.Api.Modules.Identity.Domain;

public sealed class UserAccount
{
    private UserAccount()
    {
    }

    public UserAccount(
        Guid id,
        string email,
        string displayName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(id));
        }

        Id = id;
        SetEmail(email);
        DisplayName = RequireText(displayName, nameof(displayName));
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public ICollection<ResidentMembership> Memberships { get; } =
        new List<ResidentMembership>();

    private void SetEmail(string email)
    {
        Email = RequireText(email, nameof(email));
        NormalizedEmail = Email.ToUpperInvariant();
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
