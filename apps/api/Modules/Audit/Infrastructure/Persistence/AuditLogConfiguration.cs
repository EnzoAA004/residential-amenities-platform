using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Audit.Domain;

namespace ResidentialAmenities.Api.Modules.Audit.Infrastructure.Persistence;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.ActorType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(log => log.Action)
            .HasConversion<string>()
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(log => log.TargetType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(log => log.CorrelationId)
            .HasMaxLength(AuditLog.MaxCorrelationIdLength);

        builder.Property(log => log.MetadataJson)
            .HasColumnType("jsonb");

        // No foreign keys to Identity/Reservations/Payments on purpose:
        // history must outlive operational data.

        builder.HasIndex(log => log.OccurredAtUtc);
        builder.HasIndex(log => new { log.BuildingId, log.OccurredAtUtc });
        builder.HasIndex(log => new { log.TargetType, log.TargetId, log.OccurredAtUtc });
        builder.HasIndex(log => new { log.ActorUserId, log.OccurredAtUtc });
        builder.HasIndex(log => new { log.Action, log.OccurredAtUtc });
    }
}
