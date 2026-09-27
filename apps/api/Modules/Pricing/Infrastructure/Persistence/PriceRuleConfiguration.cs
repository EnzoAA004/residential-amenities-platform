using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Modules.Pricing.Infrastructure.Persistence;

public sealed class PriceRuleConfiguration : IEntityTypeConfiguration<PriceRule>
{
    public void Configure(EntityTypeBuilder<PriceRule> builder)
    {
        builder.ToTable("PriceRules");

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.ComponentType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(rule => rule.UseType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(rule => rule.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(rule => rule.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(rule => rule.EffectiveFromUtc)
            .IsRequired();

        // No EF navigation to Amenity: Pricing references it by id only,
        // per the module-boundary rule against sharing another module's
        // entities/repositories as an integration mechanism.
        builder.HasIndex(rule => new
        {
            rule.BuildingId,
            rule.AmenityId,
            rule.ComponentType,
            rule.UseType,
            rule.EffectiveFromUtc
        });
    }
}
