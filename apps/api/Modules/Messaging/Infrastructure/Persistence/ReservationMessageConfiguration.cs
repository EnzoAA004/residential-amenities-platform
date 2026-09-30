using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Messaging.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Messaging.Infrastructure.Persistence;

public sealed class ReservationMessageConfiguration
    : IEntityTypeConfiguration<ReservationMessage>
{
    public void Configure(EntityTypeBuilder<ReservationMessage> builder)
    {
        builder.ToTable("ReservationMessages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Content)
            .HasMaxLength(ReservationMessage.MaxContentLength)
            .IsRequired();

        builder.Property(message => message.AuthorIsAdministrator)
            .IsRequired();

        builder.HasOne<Reservation>()
            .WithMany()
            .HasForeignKey(message => message.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(message => new { message.ReservationId, message.CreatedAtUtc });
    }
}
