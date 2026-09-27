using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Amenities.Domain;

namespace ResidentialAmenities.Api.Modules.Amenities.Infrastructure.Persistence;

public sealed class AmenityUnavailablePeriodConfiguration
    : IEntityTypeConfiguration<AmenityUnavailablePeriod>
{
    public void Configure(EntityTypeBuilder<AmenityUnavailablePeriod> builder)
    {
        builder.ToTable("AmenityUnavailablePeriods");

        builder.HasKey(period => period.Id);

        builder.Property(period => period.StartsAtUtc)
            .IsRequired();

        builder.Property(period => period.EndsAtUtc)
            .IsRequired();

        builder.Property(period => period.Reason)
            .HasMaxLength(280);

        builder.HasIndex(period => new
        {
            period.AmenityId,
            period.StartsAtUtc,
            period.EndsAtUtc
        });
    }
}
