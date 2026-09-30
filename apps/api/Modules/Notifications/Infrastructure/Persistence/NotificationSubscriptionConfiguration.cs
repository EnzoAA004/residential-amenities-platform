using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Modules.Notifications.Domain;

namespace ResidentialAmenities.Api.Modules.Notifications.Infrastructure.Persistence;

public sealed class NotificationSubscriptionConfiguration
    : IEntityTypeConfiguration<NotificationSubscription>
{
    public void Configure(EntityTypeBuilder<NotificationSubscription> builder)
    {
        builder.HasKey(subscription => subscription.Id);

        builder
            .Property(subscription => subscription.Platform)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder
            .Property(subscription => subscription.Endpoint)
            .HasMaxLength(2048)
            .IsRequired();

        builder
            .Property(subscription => subscription.EndpointHash)
            .HasMaxLength(64)
            .IsRequired();

        builder
            .Property(subscription => subscription.P256Dh)
            .HasMaxLength(512)
            .IsRequired();

        builder
            .Property(subscription => subscription.Auth)
            .HasMaxLength(512)
            .IsRequired();

        builder
            .Property(subscription => subscription.UserAgent)
            .HasMaxLength(512);

        builder.HasIndex(subscription => subscription.UserId);

        builder
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(subscription => subscription.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(subscription => new { subscription.UserId, subscription.EndpointHash })
            .IsUnique();
    }
}
