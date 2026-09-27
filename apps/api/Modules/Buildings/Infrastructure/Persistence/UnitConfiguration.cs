using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Buildings.Domain;

namespace ResidentialAmenities.Api.Modules.Buildings.Infrastructure.Persistence;

public sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("Units");

        builder.HasKey(unit => unit.Id);

        builder.HasAlternateKey(unit => new
        {
            unit.BuildingId,
            unit.Id
        });

        builder.Property(unit => unit.Floor)
            .IsRequired();

        builder.Property(unit => unit.Door)
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(unit => unit.Label)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(unit => unit.IsActive)
            .IsRequired();

        builder.HasIndex(unit => new
        {
            unit.BuildingId,
            unit.Label
        }).IsUnique();

        builder.HasIndex(unit => new
        {
            unit.BuildingId,
            unit.Floor,
            unit.Door
        }).IsUnique();
    }
}
