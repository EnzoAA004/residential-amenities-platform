using Microsoft.AspNetCore.Identity;
using ResidentialAmenities.Api.Modules.Buildings.Domain;

namespace ResidentialAmenities.Api.Modules.Identity.Domain;

public sealed class UserAccount : IdentityUser<Guid>
{
    public UserAccount()
    {
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public UserAccount(
        Guid id,
        string email,
        string displayName) : this()
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(id));
        }

        Id = id;
        SetEmailIdentity(email);
        DisplayName = RequireText(displayName, nameof(displayName));
    }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<ResidentMembership> Memberships { get; } =
        new List<ResidentMembership>();

    private void SetEmailIdentity(string email)
    {
        Email = RequireText(email, nameof(email));
        NormalizedEmail = Email.ToUpperInvariant();
        UserName = Email;
        NormalizedUserName = NormalizedEmail;
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
