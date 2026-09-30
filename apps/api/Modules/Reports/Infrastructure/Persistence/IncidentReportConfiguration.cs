using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Reports.Domain;

namespace ResidentialAmenities.Api.Modules.Reports.Infrastructure.Persistence;

public sealed class IncidentReportConfiguration : IEntityTypeConfiguration<IncidentReport>
{
    public void Configure(EntityTypeBuilder<IncidentReport> builder)
    {
        builder.ToTable("IncidentReports");

        builder.HasKey(report => report.Id);

        builder.Property(report => report.Content)
            .HasMaxLength(IncidentReport.MaxContentLength)
            .IsRequired();

        builder.Property(report => report.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasMany(report => report.Attachments)
            .WithOne()
            .HasForeignKey(attachment => attachment.IncidentReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(IncidentReport.Attachments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(report => new { report.BuildingId, report.CreatedAtUtc });

        builder.HasIndex(report => report.ReportedByUserId);
    }
}
