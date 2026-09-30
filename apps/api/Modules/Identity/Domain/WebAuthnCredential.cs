namespace ResidentialAmenities.Api.Modules.Identity.Domain;

/// <summary>
/// A WebAuthn/passkey public-key credential registered for biometric
/// sign-in (issue #94, ADR-012). Only the public key, credential id and a
/// signature counter are ever stored — no biometric data (template, face
/// data, or anything derived from one) ever reaches the backend; the
/// device/OS validates the biometric locally and only a standard
/// public-key signature crosses the network.
/// </summary>
public sealed class WebAuthnCredential
{
    private WebAuthnCredential()
    {
    }

    public WebAuthnCredential(
        Guid id,
        Guid userId,
        byte[] credentialId,
        byte[] publicKey,
        uint signCount,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Credential id is required.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (credentialId is null || credentialId.Length == 0)
        {
            throw new ArgumentException("Credential id bytes are required.", nameof(credentialId));
        }

        if (publicKey is null || publicKey.Length == 0)
        {
            throw new ArgumentException("Public key is required.", nameof(publicKey));
        }

        Id = id;
        UserId = userId;
        CredentialId = credentialId;
        PublicKey = publicKey;
        SignCount = signCount;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public byte[] CredentialId { get; private set; } = [];

    public byte[] PublicKey { get; private set; } = [];

    /// <summary>
    /// The authenticator's own signature counter, used to detect a cloned
    /// authenticator (a replayed assertion would present a counter that
    /// does not advance). Updated after every successful assertion.
    /// </summary>
    public uint SignCount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    public void RecordUse(uint newSignCount, DateTimeOffset usedAtUtc)
    {
        SignCount = newSignCount;
        LastUsedAtUtc = usedAtUtc;
    }
}
