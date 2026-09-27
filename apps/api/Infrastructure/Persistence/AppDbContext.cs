using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Building> Buildings => Set<Building>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<ResidentMembership> ResidentMemberships =>
        Set<ResidentMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
