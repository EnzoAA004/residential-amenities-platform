using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Media.Domain;

namespace ResidentialAmenities.Api.Modules.Media.Infrastructure.Persistence;

public sealed class MediaAttachmentConfiguration : IEntityTypeConfiguration<MediaAttachment>
{
    public void Configure(EntityTypeBuilder<MediaAttachment> builder)
    {
        builder.ToTable("MediaAttachments");

        builder.HasKey(attachment => attachment.Id);

        builder.Property(attachment => attachment.ContentType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(attachment => attachment.ObjectKey)
            .HasMaxLength(260)
            .IsRequired();

        builder.HasIndex(attachment => attachment.ObjectKey).IsUnique();

        builder.HasIndex(attachment => attachment.UploadedByUserId);
    }
}
