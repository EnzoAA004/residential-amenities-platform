using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Amenities.Domain;

namespace ResidentialAmenities.Api.Modules.Amenities.Infrastructure.Persistence;

public sealed class AmenityConfiguration : IEntityTypeConfiguration<Amenity>
{
    public void Configure(EntityTypeBuilder<Amenity> builder)
    {
        builder.ToTable("Amenities");

        builder.HasKey(amenity => amenity.Id);

        builder.HasAlternateKey(amenity => new
        {
            amenity.BuildingId,
            amenity.Id
        });

        builder.Property(amenity => amenity.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(amenity => amenity.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(amenity => amenity.AllowsSharedUse)
            .IsRequired();

        builder.Property(amenity => amenity.AllowsExclusiveUse)
            .IsRequired();

        builder.Property(amenity => amenity.IsActive)
            .IsRequired();

        builder.HasIndex(amenity => new
        {
            amenity.BuildingId,
            amenity.Name
        }).IsUnique();

        builder.HasMany(amenity => amenity.AvailabilityWindows)
            .WithOne(window => window.Amenity)
            .HasForeignKey(window => window.AmenityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(amenity => amenity.UnavailablePeriods)
            .WithOne(period => period.Amenity)
            .HasForeignKey(period => period.AmenityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Amenity.AvailabilityWindows))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Metadata
            .FindNavigation(nameof(Amenity.UnavailablePeriods))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
