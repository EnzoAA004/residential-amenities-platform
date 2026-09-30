namespace ResidentialAmenities.Api.Modules.Identity.Domain;

/// <summary>
/// A single-use, short-lived verification code (issue #93). Only
/// <see cref="CodeHash"/> is ever stored — the plaintext code is emailed to
/// the resident and never persisted anywhere. Deliberately mirrors the
/// hash-only pattern already used for push-notification endpoints
/// (#77's <c>NotificationSubscription.EndpointHash</c>): a fast SHA-256
/// hash is appropriate here too, because this is a short-lived, rate-
/// limited, single-use secret rather than a long-term credential (which is
/// what a slow password hash like PBKDF2/bcrypt is for — that role belongs
/// entirely to ASP.NET Core Identity's own password hasher, never
/// reimplemented here).
/// </summary>
public sealed class VerificationCode
{
    public const int MaxAttempts = 5;

    private VerificationCode()
    {
    }

    public VerificationCode(
        Guid id,
        Guid userId,
        VerificationCodePurpose purpose,
        string codeHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Verification code id is required.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(codeHash))
        {
            throw new ArgumentException("Code hash is required.", nameof(codeHash));
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Expiration must be after creation.", nameof(expiresAtUtc));
        }

        Id = id;
        UserId = userId;
        Purpose = purpose;
        CodeHash = codeHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        AttemptCount = 0;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public VerificationCodePurpose Purpose { get; private set; }

    public string CodeHash { get; private set; } = string.Empty;

    public int AttemptCount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    /// <summary>
    /// True only when this code could still possibly succeed: not already
    /// used, not expired, and not already exhausted by wrong guesses. A
    /// caller must check this before comparing hashes — a code that has
    /// used up its attempt budget is treated as dead even if the very next
    /// guess would have been correct.
    /// </summary>
    public bool IsUsable(DateTimeOffset nowUtc) =>
        ConsumedAtUtc is null && nowUtc < ExpiresAtUtc && AttemptCount < MaxAttempts;

    public void RegisterFailedAttempt() => AttemptCount++;

    /// <returns>true only when this call performed the consumption (idempotency guard).</returns>
    public bool Consume(DateTimeOffset consumedAtUtc)
    {
        if (ConsumedAtUtc is not null)
        {
            return false;
        }

        ConsumedAtUtc = consumedAtUtc;
        return true;
    }
}
