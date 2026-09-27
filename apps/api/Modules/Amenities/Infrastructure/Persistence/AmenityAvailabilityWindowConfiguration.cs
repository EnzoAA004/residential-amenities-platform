using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Amenities.Domain;

namespace ResidentialAmenities.Api.Modules.Amenities.Infrastructure.Persistence;

public sealed class AmenityAvailabilityWindowConfiguration
    : IEntityTypeConfiguration<AmenityAvailabilityWindow>
{
    public void Configure(EntityTypeBuilder<AmenityAvailabilityWindow> builder)
    {
        builder.ToTable("AmenityAvailabilityWindows");

        builder.HasKey(window => window.Id);

        builder.Property(window => window.DayOfWeek)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(window => window.StartTime)
            .IsRequired();

        builder.Property(window => window.EndTime)
            .IsRequired();

        builder.HasIndex(window => new
        {
            window.AmenityId,
            window.DayOfWeek
        });
    }
}
