using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Domain;

namespace ResidentialAmenities.Api.Modules.Audit.Application;

public sealed class AuditRecorder(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor) : IAuditRecorder
{
    public void Record(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        dbContext.AuditLogs.Add(new AuditLog(
            Guid.NewGuid(),
            timeProvider.GetUtcNow(),
            record.BuildingId,
            record.ActorType,
            record.ActorUserId,
            record.Action,
            record.TargetType,
            record.TargetId,
            // Requests reuse the ASP.NET Core trace id; background jobs have
            // no request, so NULL.
            httpContextAccessor.HttpContext?.TraceIdentifier,
            record.Metadata?.Json));
    }
}
