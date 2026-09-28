using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Infrastructure.Persistence;

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");

        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.UseType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(reservation => reservation.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(reservation => reservation.StartsAtUtc)
            .IsRequired();

        builder.Property(reservation => reservation.EndsAtUtc)
            .IsRequired();

        builder.Property(reservation => reservation.CreatedAtUtc)
            .IsRequired();

        builder.Property(reservation => reservation.CancellationReason)
            .HasMaxLength(Reservation.MaxCancellationReasonLength);

        builder.Property(reservation => reservation.ExpiresAtUtc)
            .IsRequired();

        builder.HasMany(reservation => reservation.Resources)
            .WithOne(resource => resource.Reservation)
            .HasForeignKey(resource => resource.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(reservation => reservation.PriceLines)
            .WithOne(line => line.Reservation)
            .HasForeignKey(line => line.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Reservation.Resources))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Metadata
            .FindNavigation(nameof(Reservation.PriceLines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Supports the conflict-detection query (Building + time range) and
        // history/status filtering. Amenity-scoped conflict lookups go
        // through ReservationResource's own index.
        builder.HasIndex(reservation => new
        {
            reservation.BuildingId,
            reservation.StartsAtUtc,
            reservation.EndsAtUtc
        });

        builder.HasIndex(reservation => reservation.Status);

        // Supports the expiration job's bulk UPDATE ... WHERE Status =
        // 'Pending' AND ExpiresAtUtc <= @now.
        builder.HasIndex(reservation => new
        {
            reservation.Status,
            reservation.ExpiresAtUtc
        });
    }
}
