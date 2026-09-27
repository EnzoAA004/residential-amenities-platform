using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Infrastructure.Persistence;

public sealed class ReservationPriceLineConfiguration
    : IEntityTypeConfiguration<ReservationPriceLine>
{
    public void Configure(EntityTypeBuilder<ReservationPriceLine> builder)
    {
        builder.ToTable("ReservationPriceLines");

        builder.HasKey(line => line.Id);

        builder.Property(line => line.ComponentType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(line => line.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(line => line.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(line => line.QuotedAtUtc)
            .IsRequired();

        // PriceRuleId/AmenityId are plain historical ids: no FK to
        // PriceRules or Amenities, so this snapshot stays valid even if
        // that rule is later changed or removed (RB-008).
        builder.HasIndex(line => line.ReservationId);
    }
}
