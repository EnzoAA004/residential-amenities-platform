using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Modules.Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/auth")
            .WithTags("Identity");

        group.MapPost(
            "/login",
            LoginAsync);

        group.MapPost(
            "/refresh",
            RefreshAsync);

        group.MapPost(
                "/logout",
                LogoutAsync)
            .RequireAuthorization();

        group.MapGet(
                "/me",
                GetCurrentUserAsync)
            .RequireAuthorization();

        group.MapGet(
                "/check/resident",
                () => Results.Ok(new { access = ApplicationRoles.Resident }))
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        group.MapGet(
                "/check/admin",
                () => Results.Ok(
                    new { access = ApplicationRoles.Administrator }))
            .RequireAuthorization(AuthorizationPolicies.Administrator);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest login,
        bool? useCookies,
        bool? useSessionCookies,
        SignInManager<UserAccount> signInManager,
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        IAuditRecorder auditRecorder,
        ILogger<AuditLog> logger,
        CancellationToken cancellationToken)
    {
        var useCookieScheme =
            useCookies == true || useSessionCookies == true;

        var isPersistent =
            useCookies == true && useSessionCookies != true;

        signInManager.AuthenticationScheme = useCookieScheme
            ? IdentityConstants.ApplicationScheme
            : IdentityConstants.BearerScheme;

        var result = await signInManager.PasswordSignInAsync(
            login.Email,
            login.Password,
            isPersistent,
            lockoutOnFailure: true);

        if (result.RequiresTwoFactor)
        {
            if (!string.IsNullOrWhiteSpace(login.TwoFactorCode))
            {
                result =
                    await signInManager.TwoFactorAuthenticatorSignInAsync(
                        login.TwoFactorCode,
                        isPersistent,
                        rememberClient: isPersistent);
            }
            else if (!string.IsNullOrWhiteSpace(
                         login.TwoFactorRecoveryCode))
            {
                result =
                    await signInManager
                        .TwoFactorRecoveryCodeSignInAsync(
                            login.TwoFactorRecoveryCode);
            }
        }

        // The response is identical for every failure (no user enumeration);
        // the audit entry keeps only a general category — never the submitted
        // e-mail or password.
        if (result.Succeeded)
        {
            var user = await userManager.FindByEmailAsync(login.Email);

            await TryAuditAsync(
                dbContext,
                auditRecorder,
                logger,
                user is null
                    ? null
                    : AuditRecord.ByUser(
                        user.Id,
                        AuditAction.AuthenticationSucceeded,
                        AuditTargetType.User,
                        user.Id,
                        buildingId: null),
                cancellationToken);

            return Results.Empty;
        }

        var failure = result.IsLockedOut ? "LockedOut"
            : result.IsNotAllowed ? "NotAllowed"
            : result.RequiresTwoFactor ? "TwoFactorRequired"
            : "InvalidCredentials";

        await TryAuditAsync(
            dbContext,
            auditRecorder,
            logger,
            AuditRecord.BySystem(
                AuditAction.AuthenticationFailed,
                AuditTargetType.User,
                targetId: null,
                buildingId: null,
                AuditMetadata.AuthenticationFailure(failure)),
            cancellationToken);

        return Results.Problem(
            title: "Authentication failed.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    // Authentication has already happened; a failure to write the audit entry
    // must not turn a valid login/logout into an error (audit is history, not
    // authority), so it is logged (no personal data) and swallowed.
    private static async Task TryAuditAsync(
        AppDbContext dbContext,
        IAuditRecorder auditRecorder,
        ILogger<AuditLog> logger,
        AuditRecord? record,
        CancellationToken cancellationToken)
    {
        if (record is null)
        {
            return;
        }

        try
        {
            auditRecorder.Record(record);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(
                error,
                "Could not record audit action {Action}.",
                record.Action);
        }
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        SignInManager<UserAccount> signInManager,
        IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
        TimeProvider timeProvider)
    {
        var refreshTokenProtector =
            bearerTokenOptions
                .Get(IdentityConstants.BearerScheme)
                .RefreshTokenProtector;

        var refreshTicket =
            refreshTokenProtector.Unprotect(request.RefreshToken);

        if (refreshTicket?.Properties?.ExpiresUtc is not { } expiresUtc ||
            timeProvider.GetUtcNow() >= expiresUtc ||
            await signInManager.ValidateSecurityStampAsync(
                refreshTicket.Principal) is not { } user)
        {
            return Results.Challenge();
        }

        var principal =
            await signInManager.CreateUserPrincipalAsync(user);

        return Results.SignIn(
            principal,
            authenticationScheme: IdentityConstants.BearerScheme);
    }

    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal principal,
        SignInManager<UserAccount> signInManager,
        AppDbContext dbContext,
        IAuditRecorder auditRecorder,
        ILogger<AuditLog> logger,
        CancellationToken cancellationToken)
    {
        // Resolved before the sign-out, while the caller is still identified.
        var userId = principal.GetUserId();

        await signInManager.SignOutAsync();

        await TryAuditAsync(
            dbContext,
            auditRecorder,
            logger,
            userId is { } id
                ? AuditRecord.ByUser(
                    id,
                    AuditAction.Logout,
                    AuditTargetType.User,
                    id,
                    buildingId: null)
                : null,
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);

        var memberships = await dbContext.ResidentMemberships
            .AsNoTracking()
            .Where(membership =>
                membership.UserId == user.Id &&
                membership.Status == ResidentMembershipStatus.Active)
            .Select(membership => new
            {
                membership.BuildingId,
                membership.UnitId,
                Unit = membership.Unit.Label,
                Building = membership.Unit.Building.Name
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            Roles = roles,
            Memberships = memberships
        });
    }
}
