using ResidentialAmenities.Api.Modules.Audit.Domain;

namespace ResidentialAmenities.Api.Modules.Audit.Application;

/// <summary>
/// An audit fact to record. Built only through the actor-specific factories,
/// so the actor invariant (a user has an id; system/provider do not) is
/// stated at the call site.
/// </summary>
public sealed record AuditRecord(
    AuditAction Action,
    AuditActorType ActorType,
    Guid? ActorUserId,
    AuditTargetType TargetType,
    Guid? TargetId,
    Guid? BuildingId,
    AuditMetadata? Metadata)
{
    public static AuditRecord ByUser(
        Guid actorUserId,
        AuditAction action,
        AuditTargetType targetType,
        Guid? targetId,
        Guid? buildingId,
        AuditMetadata? metadata = null) =>
        new(action, AuditActorType.User, actorUserId, targetType, targetId, buildingId, metadata);

    public static AuditRecord BySystem(
        AuditAction action,
        AuditTargetType targetType,
        Guid? targetId,
        Guid? buildingId,
        AuditMetadata? metadata = null) =>
        new(action, AuditActorType.System, null, targetType, targetId, buildingId, metadata);

    public static AuditRecord ByExternalProvider(
        AuditAction action,
        AuditTargetType targetType,
        Guid? targetId,
        Guid? buildingId,
        AuditMetadata? metadata = null) =>
        new(action, AuditActorType.ExternalProvider, null, targetType, targetId, buildingId, metadata);
}
