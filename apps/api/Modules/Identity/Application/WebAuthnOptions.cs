namespace ResidentialAmenities.Api.Modules.Identity.Application;

/// <summary>
/// WebAuthn/passkey configuration (issue #94, ADR-012), bound from the
/// <c>WebAuthn</c> section. Kept as our own small options type (rather than
/// binding <c>Fido2NetLib.Fido2Configuration</c> directly) because its
/// <c>Origins</c> is an <c>IReadOnlySet&lt;string&gt;</c>, which
/// <see cref="Microsoft.Extensions.Configuration"/> does not bind cleanly —
/// this is mapped into a <c>Fido2Configuration</c> once at startup instead.
/// </summary>
public sealed class WebAuthnOptions
{
    public const string SectionName = "WebAuthn";

    /// <summary>
    /// The Relying Party ID — the domain the passkey is scoped to. Must be
    /// the page's domain or a registrable parent of it; defaults to a
    /// local development value.
    /// </summary>
    public string RelyingPartyId { get; set; } = "localhost";

    public string RelyingPartyName { get; set; } = "Residential Amenities";

    /// <summary>Exact origins (scheme + host + port) the client is served from.</summary>
    public string[] Origins { get; set; } = ["http://localhost:4200", "https://localhost:4200"];
}
