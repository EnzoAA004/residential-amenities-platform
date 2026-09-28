using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Payments.Domain;

namespace ResidentialAmenities.Api.Modules.Payments.Infrastructure.Persistence;

public sealed class PaymentProviderEventConfiguration
    : IEntityTypeConfiguration<PaymentProviderEvent>
{
    public void Configure(EntityTypeBuilder<PaymentProviderEvent> builder)
    {
        builder.ToTable("PaymentProviderEvents");

        builder.HasKey(providerEvent => providerEvent.Id);

        builder.Property(providerEvent => providerEvent.Provider)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(providerEvent => providerEvent.ProviderEventId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(providerEvent => providerEvent.ProviderOrderId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(providerEvent => providerEvent.EventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(providerEvent => providerEvent.ProcessingResult)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Ignore(providerEvent => providerEvent.IsProcessed);

        builder.HasIndex(providerEvent => new
        {
            providerEvent.Provider,
            providerEvent.ProviderEventId
        }).IsUnique();

        builder.HasIndex(providerEvent => providerEvent.ProviderOrderId);
    }
}
