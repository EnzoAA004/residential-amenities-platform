namespace ResidentialAmenities.Api.Modules.Identity.Domain;

/// <summary>
/// Issue #93: the same verification-code mechanism serves both the initial
/// account activation (after an Administrator creates a resident record)
/// and a later forgotten-password request — the two are kept as distinct
/// purposes so a code minted for one can never be replayed for the other.
/// </summary>
public enum VerificationCodePurpose
{
    AccountActivation = 0,
    PasswordReset = 1
}
