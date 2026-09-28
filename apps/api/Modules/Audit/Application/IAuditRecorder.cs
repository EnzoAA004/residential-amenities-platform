namespace ResidentialAmenities.Api.Modules.Audit.Application;

/// <summary>
/// Records an audit fact. <see cref="Record"/> only ADDS the entry to the
/// current unit of work; it never saves. The use case that owns the business
/// transition calls <c>SaveChanges</c> (inside its own transaction when it has
/// one), so the transition and its audit entry are persisted together or not
/// at all. Audit is history, never authority: no business decision reads it.
/// </summary>
public interface IAuditRecorder
{
    void Record(AuditRecord record);
}
