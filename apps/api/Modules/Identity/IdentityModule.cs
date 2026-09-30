using Fido2NetLib;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Identity.Application;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Modules.Identity.Infrastructure;

namespace ResidentialAmenities.Api.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
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

        services
            .AddOptions<CookieAuthenticationOptions>(
                IdentityConstants.ApplicationScheme)
            .Configure<IHostEnvironment>((options, environment) =>
            {
                // __Host- cookies require Secure + Path=/ and a secure
                // origin. Keep local HTTP development usable without
                // weakening the production cookie contract.
                options.Cookie.Name = environment.IsDevelopment()
                    ? "residential-auth"
                    : "__Host-residential-auth";

                options.Cookie.HttpOnly = true;
                options.Cookie.Path = "/";
                options.Cookie.Domain = null;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;

                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
            });

        services.Configure<SecurityStampValidatorOptions>(
            options =>
            {
                options.ValidationInterval =
                    TimeSpan.FromMinutes(5);
            });

        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        // Issue #94 (ADR-012): WebAuthn/passkeys for biometric sign-in.
        // Our own small WebAuthnOptions is bound from configuration and
        // mapped into Fido2Configuration here (Origins is an
        // IReadOnlySet<string>, which configuration binding does not
        // populate directly).
        services
            .AddOptions<WebAuthnOptions>()
            .Bind(configuration.GetSection(WebAuthnOptions.SectionName));

        services.AddMemoryCache();

        services.AddSingleton<IFido2>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<WebAuthnOptions>>().Value;

            return new Fido2(new Fido2Configuration
            {
                RPID = options.RelyingPartyId,
                RPName = options.RelyingPartyName,
                Origins = new HashSet<string>(options.Origins)
            });
        });

        return services;
    }
}
