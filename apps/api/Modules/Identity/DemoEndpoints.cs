using Microsoft.AspNetCore.Identity;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Modules.Identity;

/// <summary>
/// Public, resident-only staging demo access. This never creates an
/// administrator session and is disabled unless Demo:Enabled is explicitly
/// true in Development or Staging.
/// </summary>
public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/demo/status",
                (IConfiguration configuration, IHostEnvironment environment) =>
                    Results.Ok(new
                    {
                        enabled = IsEnabled(configuration, environment)
                    }))
            .WithTags("Demo")
            .AllowAnonymous();

        endpoints
            .MapPost(
                "/api/demo/session",
                CreateDemoSessionAsync)
            .WithTags("Demo")
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> CreateDemoSessionAsync(
        IConfiguration configuration,
        IHostEnvironment environment,
        UserManager<UserAccount> userManager,
        SignInManager<UserAccount> signInManager)
    {
        if (!IsEnabled(configuration, environment))
        {
            return Results.NotFound();
        }

        var user =
            await userManager.FindByEmailAsync(
                DemoDataSeeder.DemoResidentEmail);

        if (user is null ||
            !user.IsActive ||
            !await userManager.IsInRoleAsync(
                user,
                ApplicationRoles.Resident) ||
            await userManager.IsInRoleAsync(
                user,
                ApplicationRoles.Administrator))
        {
            return Results.Problem(
                title: "Demo session is unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        signInManager.AuthenticationScheme =
            IdentityConstants.ApplicationScheme;

        await signInManager.SignInAsync(
            user,
            isPersistent: false);

        return Results.NoContent();
    }

    private static bool IsEnabled(
        IConfiguration configuration,
        IHostEnvironment environment) =>
        configuration.GetValue<bool>("Demo:Enabled") &&
        (environment.IsDevelopment() || environment.IsStaging());
}
