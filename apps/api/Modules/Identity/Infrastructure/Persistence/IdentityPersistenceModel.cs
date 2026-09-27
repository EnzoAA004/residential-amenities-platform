using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ResidentialAmenities.Api.Modules.Identity.Infrastructure.Persistence;

public static class IdentityPersistenceModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityRole<Guid>>(builder =>
        {
            builder.ToTable("Roles");

            builder.HasData(
                new IdentityRole<Guid>
                {
                    Id = ApplicationRoles.ResidentId,
                    Name = ApplicationRoles.Resident,
                    NormalizedName = ApplicationRoles.Resident.ToUpperInvariant(),
                    ConcurrencyStamp =
                        ApplicationRoles.ResidentConcurrencyStamp
                },
                new IdentityRole<Guid>
                {
                    Id = ApplicationRoles.AdministratorId,
                    Name = ApplicationRoles.Administrator,
                    NormalizedName =
                        ApplicationRoles.Administrator.ToUpperInvariant(),
                    ConcurrencyStamp =
                        ApplicationRoles.AdministratorConcurrencyStamp
                });
        });

        modelBuilder.Entity<IdentityUserRole<Guid>>()
            .ToTable("UserRoles");

        modelBuilder.Entity<IdentityUserClaim<Guid>>()
            .ToTable("UserClaims");

        modelBuilder.Entity<IdentityUserLogin<Guid>>()
            .ToTable("UserLogins");

        modelBuilder.Entity<IdentityUserToken<Guid>>()
            .ToTable("UserTokens");

        modelBuilder.Entity<IdentityRoleClaim<Guid>>()
            .ToTable("RoleClaims");
    }
}
