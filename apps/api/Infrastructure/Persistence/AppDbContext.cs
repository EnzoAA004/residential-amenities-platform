using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Modules.Identity.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options)
    : IdentityDbContext<UserAccount, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Building> Buildings => Set<Building>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<ResidentMembership> ResidentMemberships =>
        Set<ResidentMembership>();

    public DbSet<Amenity> Amenities => Set<Amenity>();

    public DbSet<AmenityAvailabilityWindow> AmenityAvailabilityWindows =>
        Set<AmenityAvailabilityWindow>();

    public DbSet<AmenityUnavailablePeriod> AmenityUnavailablePeriods =>
        Set<AmenityUnavailablePeriod>();

    public DbSet<PriceRule> PriceRules => Set<PriceRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);

        IdentityPersistenceModel.Configure(modelBuilder);
    }
}
