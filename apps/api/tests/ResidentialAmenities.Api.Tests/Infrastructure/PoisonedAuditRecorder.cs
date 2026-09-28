using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;

namespace ResidentialAmenities.Api.Tests.Infrastructure;

/// <summary>
/// An audit recorder that queues a row PostgreSQL rejects (invalid jsonb) in
/// the very SaveChanges that carries the business change, to prove the change
/// and its audit entry are atomic on real PostgreSQL.
/// </summary>
public sealed class PoisonedAuditRecorder(AppDbContext db, TimeProvider clock) : IAuditRecorder
{
    public void Record(AuditRecord record) =>
        db.AuditLogs.Add(new AuditLog(
            Guid.NewGuid(),
            clock.GetUtcNow(),
            record.BuildingId,
            record.ActorType,
            record.ActorUserId,
            record.Action,
            record.TargetType,
            record.TargetId,
            null,
            "this is not json"));
}
