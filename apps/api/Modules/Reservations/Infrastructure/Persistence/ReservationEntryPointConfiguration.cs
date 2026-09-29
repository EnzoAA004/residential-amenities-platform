using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Infrastructure.Persistence;

public sealed class ReservationEntryPointConfiguration
    : IEntityTypeConfiguration<ReservationEntryPoint>
{
    public void Configure(EntityTypeBuilder<ReservationEntryPoint> builder)
    {
        builder.ToTable("ReservationEntryPoints");

        builder.HasKey(entryPoint => entryPoint.Id);

        builder.Property(entryPoint => entryPoint.Token)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(entryPoint => entryPoint.SuggestedUseType)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(entryPoint => entryPoint.DisplayName)
            .HasMaxLength(160);

        builder.Property(entryPoint => entryPoint.IsActive)
            .IsRequired();

        builder.HasIndex(entryPoint => entryPoint.Token)
            .IsUnique();

        builder.HasIndex(entryPoint => new
        {
            entryPoint.BuildingId,
            entryPoint.AmenityId
        });
    }
}
