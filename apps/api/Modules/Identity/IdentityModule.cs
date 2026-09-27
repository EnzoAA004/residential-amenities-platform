using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services)
    {
        services
            .AddIdentityApiEndpoints<UserAccount>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan =
                    TimeSpan.FromMinutes(15);

                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.ResidentAccess,
                policy => policy.RequireRole(
                    ApplicationRoles.Resident,
                    ApplicationRoles.Administrator));

            options.AddPolicy(
                AuthorizationPolicies.Administrator,
                policy => policy.RequireRole(
                    ApplicationRoles.Administrator));
        });

        services.Configure<BearerTokenOptions>(
            IdentityConstants.BearerScheme,
            options =>
            {
                options.BearerTokenExpiration =
                    TimeSpan.FromMinutes(20);

                options.RefreshTokenExpiration =
                    TimeSpan.FromDays(14);
            });

        services.Configure<CookieAuthenticationOptions>(
            IdentityConstants.ApplicationScheme,
            options =>
            {
                options.Cookie.Name = "__Host-residential-auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy =
                    CookieSecurePolicy.SameAsRequest;

                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
            });

        services.Configure<SecurityStampValidatorOptions>(
            options =>
            {
                options.ValidationInterval =
                    TimeSpan.FromMinutes(5);
            });

        return services;
    }
}
