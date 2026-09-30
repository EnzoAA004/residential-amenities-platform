using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Infrastructure.Persistence;

public sealed class EventSlotDefinitionConfiguration
    : IEntityTypeConfiguration<EventSlotDefinition>
{
    public void Configure(EntityTypeBuilder<EventSlotDefinition> builder)
    {
        builder.ToTable("EventSlotDefinitions");

        builder.HasKey(slot => slot.Id);

        builder.Property(slot => slot.Name)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(slot => slot.StartTime)
            .IsRequired();

        builder.Property(slot => slot.EndTime)
            .IsRequired();

        builder.Property(slot => slot.IsOvernight)
            .IsRequired();

        builder.Property(slot => slot.IsActive)
            .IsRequired();

        builder.HasIndex(slot => new
        {
            slot.BuildingId,
            slot.StartTime,
            slot.EndTime
        }).IsUnique();
    }
}
