using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Buildings.Domain;

namespace ResidentialAmenities.Api.Modules.Buildings.Infrastructure.Persistence;

public sealed class BuildingConfiguration : IEntityTypeConfiguration<Building>
{
    public void Configure(EntityTypeBuilder<Building> builder)
    {
        builder.ToTable("Buildings");

        builder.HasKey(building => building.Id);

        builder.Property(building => building.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(building => building.TimeZoneId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(building => building.IsActive)
            .IsRequired();

        builder.HasMany(building => building.Units)
            .WithOne(unit => unit.Building)
            .HasForeignKey(unit => unit.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
