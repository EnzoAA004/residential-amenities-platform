using Microsoft.AspNetCore.Identity;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Infrastructure.Persistence;

/// <summary>
/// Creates or refreshes the private staging administrator configured by the
/// deployment pipeline. Credentials are supplied at runtime through Container
/// Apps secrets and are never committed to source control or Terraform state.
/// </summary>
public static class StagingAdminSeeder
{
    public static async Task SeedStagingAdministratorAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("StagingAdmin:Enabled"))
        {
            return;
        }

        var email = configuration["StagingAdmin:Email"]?.Trim();
        var password = configuration["StagingAdmin:Password"];
        var displayName = configuration["StagingAdmin:DisplayName"]?.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "StagingAdmin is enabled but its email/password configuration is missing.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "Administrador";
        }

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new UserAccount(Guid.NewGuid(), email, displayName)
            {
                EmailConfirmed = true,
                IsActive = true
            };

            var createResult = await userManager.CreateAsync(user, password);
            EnsureSucceeded(createResult, "Could not create the staging administrator");
        }
        else
        {
            user.DisplayName = displayName;
            user.EmailConfirmed = true;
            user.IsActive = true;

            var updateResult = await userManager.UpdateAsync(user);
            EnsureSucceeded(updateResult, "Could not update the staging administrator");

            if (await userManager.HasPasswordAsync(user))
            {
                var removePasswordResult = await userManager.RemovePasswordAsync(user);
                EnsureSucceeded(
                    removePasswordResult,
                    "Could not remove the previous staging administrator password");
            }

            var passwordResult = await userManager.AddPasswordAsync(user, password);
            EnsureSucceeded(passwordResult, "Could not refresh the staging administrator password");
        }

        if (!await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator))
        {
            var roleResult = await userManager.AddToRoleAsync(user, ApplicationRoles.Administrator);
            EnsureSucceeded(roleResult, "Could not grant the Administrator role");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException(
            message + ": " + string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
