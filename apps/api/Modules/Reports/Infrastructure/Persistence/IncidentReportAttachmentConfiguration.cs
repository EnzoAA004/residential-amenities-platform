using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Reports.Domain;

namespace ResidentialAmenities.Api.Modules.Reports.Infrastructure.Persistence;

public sealed class IncidentReportAttachmentConfiguration
    : IEntityTypeConfiguration<IncidentReportAttachment>
{
    public void Configure(EntityTypeBuilder<IncidentReportAttachment> builder)
    {
        builder.ToTable("IncidentReportAttachments");

        builder.HasKey(attachment => attachment.Id);

        builder.HasIndex(attachment => attachment.MediaAttachmentId).IsUnique();
    }
}
