using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Infrastructure.Persistence;
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
        SignInManager<UserAccount> signInManager)
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

        return result.Succeeded
            ? Results.Empty
            : Results.Problem(
                title: "Authentication failed.",
                statusCode: StatusCodes.Status401Unauthorized);
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
        SignInManager<UserAccount> signInManager)
    {
        await signInManager.SignOutAsync();
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
