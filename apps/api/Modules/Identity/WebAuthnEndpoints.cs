using System.Security.Claims;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Modules.Identity;

/// <summary>
/// Mobile biometric sign-in via WebAuthn/passkeys (issue #94, ADR-012).
/// The device/OS validates the biometric locally; this module never
/// receives or stores anything beyond a standard public-key credential
/// (id, public key, signature counter) — never a fingerprint template,
/// face data, or anything derived from one. Registration requires an
/// already-authenticated session (biometric sign-in unlocks an existing
/// account, per #93 — it never creates one); login is anonymous by
/// necessity but never confirms more than "biometric sign-in is not
/// available for this account", so the client always has a safe fallback
/// to the unchanged email+password form.
/// </summary>
public static class WebAuthnEndpoints
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapWebAuthnEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/auth/webauthn")
            .WithTags("Identity");

        group.MapPost("/register/options", RegisterOptionsAsync).RequireAuthorization();
        group.MapPost("/register", RegisterAsync).RequireAuthorization();
        group.MapPost("/login/options", LoginOptionsAsync).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> RegisterOptionsAsync(
        ClaimsPrincipal principal,
        UserManager<UserAccount> userManager,
        IFido2 fido2,
        IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                Id = user.Id.ToByteArray(),
                Name = user.Email!,
                DisplayName = user.DisplayName
            },
            ExcludeCredentials = [],
            AuthenticatorSelection = new AuthenticatorSelection
            {
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None,
            PubKeyCredParams = [PubKeyCredParam.ES256]
        });

        cache.Set(RegistrationCacheKey(user.Id), options, ChallengeLifetime);

        return Results.Content(options.ToJson(), "application/json");
    }

    private static async Task<IResult> RegisterAsync(
        AuthenticatorAttestationRawResponse attestationResponse,
        ClaimsPrincipal principal,
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        IFido2 fido2,
        IMemoryCache cache,
        IAuditRecorder auditRecorder,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!cache.TryGetValue(RegistrationCacheKey(user.Id), out CredentialCreateOptions? originalOptions) ||
            originalOptions is null)
        {
            return Results.Problem(
                title: "The registration attempt has expired. Please try again.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        cache.Remove(RegistrationCacheKey(user.Id));

        RegisteredPublicKeyCredential result;

        try
        {
            result = await fido2.MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = attestationResponse,
                    OriginalOptions = originalOptions,
                    IsCredentialIdUniqueToUserCallback = async (p, ct) =>
                        !await dbContext.WebAuthnCredentials.AnyAsync(c => c.CredentialId == p.CredentialId, ct)
                },
                cancellationToken);
        }
        catch (Fido2VerificationException error)
        {
            return Results.Problem(
                title: "Unable to register this credential.",
                detail: error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var nowUtc = timeProvider.GetUtcNow();

        dbContext.WebAuthnCredentials.Add(new WebAuthnCredential(
            Guid.NewGuid(), user.Id, result.Id, result.PublicKey, result.SignCount, nowUtc));

        auditRecorder.Record(AuditRecord.ByUser(
            user.Id, AuditAction.BiometricCredentialRegistered, AuditTargetType.User, user.Id, buildingId: null));

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> LoginOptionsAsync(
        LoginOptionsRequest request,
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        IFido2 fido2,
        IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email ?? string.Empty);

        if (user is null)
        {
            return Results.Ok(new LoginOptionsResponse(Available: false, SessionId: null, OptionsJson: null));
        }

        var credentials = await dbContext.WebAuthnCredentials
            .AsNoTracking()
            .Where(credential => credential.UserId == user.Id)
            .ToListAsync(cancellationToken);

        if (credentials.Count == 0)
        {
            return Results.Ok(new LoginOptionsResponse(Available: false, SessionId: null, OptionsJson: null));
        }

        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = credentials
                .Select(credential => new PublicKeyCredentialDescriptor(credential.CredentialId))
                .ToList(),
            UserVerification = UserVerificationRequirement.Required
        });

        var sessionId = Guid.NewGuid();
        cache.Set(LoginCacheKey(sessionId), new LoginChallenge(options, user.Id), ChallengeLifetime);

        return Results.Ok(new LoginOptionsResponse(Available: true, SessionId: sessionId, OptionsJson: options.ToJson()));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        SignInManager<UserAccount> signInManager,
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        IFido2 fido2,
        IMemoryCache cache,
        IAuditRecorder auditRecorder,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue(LoginCacheKey(request.SessionId), out LoginChallenge? challenge) || challenge is null)
        {
            return Results.Problem(
                title: "The sign-in attempt has expired. Please try again.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        cache.Remove(LoginCacheKey(request.SessionId));

        var credential = await dbContext.WebAuthnCredentials
            .SingleOrDefaultAsync(
                candidate => candidate.CredentialId == request.AssertionResponse.RawId && candidate.UserId == challenge.UserId,
                cancellationToken);

        if (credential is null)
        {
            return Results.Problem(title: "Unknown credential.", statusCode: StatusCodes.Status400BadRequest);
        }

        VerifyAssertionResult result;

        try
        {
            result = await fido2.MakeAssertionAsync(
                new MakeAssertionParams
                {
                    AssertionResponse = request.AssertionResponse,
                    OriginalOptions = challenge.Options,
                    StoredPublicKey = credential.PublicKey,
                    StoredSignatureCounter = credential.SignCount,
                    IsUserHandleOwnerOfCredentialIdCallback = (_, _) => Task.FromResult(true)
                },
                cancellationToken);
        }
        catch (Fido2VerificationException error)
        {
            return Results.Problem(
                title: "Biometric sign-in failed.",
                detail: error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await userManager.FindByIdAsync(challenge.UserId.ToString());

        if (user is null)
        {
            return Results.Problem(title: "Biometric sign-in failed.", statusCode: StatusCodes.Status400BadRequest);
        }

        var nowUtc = timeProvider.GetUtcNow();
        credential.RecordUse(result.SignCount, nowUtc);

        auditRecorder.Record(AuditRecord.ByUser(
            user.Id, AuditAction.BiometricSignInSucceeded, AuditTargetType.User, user.Id, buildingId: null));

        await dbContext.SaveChangesAsync(cancellationToken);

        signInManager.AuthenticationScheme = IdentityConstants.ApplicationScheme;
        await signInManager.SignInAsync(user, isPersistent: true, authenticationMethod: "webauthn");

        return Results.Empty;
    }

    private static string RegistrationCacheKey(Guid userId) => $"webauthn-register:{userId}";

    private static string LoginCacheKey(Guid sessionId) => $"webauthn-login:{sessionId}";

    private sealed record LoginChallenge(AssertionOptions Options, Guid UserId);

    private sealed record LoginOptionsRequest(string? Email);

    private sealed record LoginOptionsResponse(bool Available, Guid? SessionId, string? OptionsJson);

    private sealed record LoginRequest(Guid SessionId, AuthenticatorAssertionRawResponse AssertionResponse);
}
