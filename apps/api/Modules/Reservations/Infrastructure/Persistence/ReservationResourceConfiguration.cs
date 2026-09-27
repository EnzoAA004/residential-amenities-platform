using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Infrastructure.Persistence;

public sealed class ReservationResourceConfiguration
    : IEntityTypeConfiguration<ReservationResource>
{
    public void Configure(EntityTypeBuilder<ReservationResource> builder)
    {
        builder.ToTable("ReservationResources");

        builder.HasKey(resource => resource.Id);

        builder.Property(resource => resource.IsExclusive)
            .IsRequired();

        // No FK/navigation to Amenities: referenced by id only, per the
        // module-boundary rule against cross-module entity sharing.
        builder.HasIndex(resource => new
        {
            resource.AmenityId,
            resource.ReservationId
        });
    }
}
