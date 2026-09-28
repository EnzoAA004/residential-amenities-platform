namespace ResidentialAmenities.Api.Modules.Audit.Domain;

/// <summary>
/// One immutable business fact: who did what, when, to which object, in which
/// building. Append-only (RF-021, RNF-007): there is no public mutator, no
/// endpoint edits or deletes it, and <c>AppDbContext</c> refuses to save a
/// modified or deleted <see cref="AuditLog"/>. A correction is a new event.
///
/// No foreign keys point to Identity/Reservations/Payments: history must
/// survive changes to operational data, and the ids are plain historical
/// references. Audit records history; it is never the authority for whether a
/// reservation is available, a payment approved or a user authorized.
/// </summary>
public sealed class AuditLog
{
    public const int MaxCorrelationIdLength = 100;

    private AuditLog()
    {
    }

    public AuditLog(
        Guid id,
        DateTimeOffset occurredAtUtc,
        Guid? buildingId,
        AuditActorType actorType,
        Guid? actorUserId,
        AuditAction action,
        AuditTargetType targetType,
        Guid? targetId,
        string? correlationId,
        string? metadataJson)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Audit id is required.", nameof(id));
        }

        if (!Enum.IsDefined(action))
        {
            throw new ArgumentException("A known audit action is required.", nameof(action));
        }

        if (!Enum.IsDefined(targetType))
        {
            throw new ArgumentException("A known target type is required.", nameof(targetType));
        }

        if (!Enum.IsDefined(actorType))
        {
            throw new ArgumentException("A known actor type is required.", nameof(actorType));
        }

        if (actorType == AuditActorType.User && (actorUserId is null || actorUserId == Guid.Empty))
        {
            throw new ArgumentException(
                "A user actor requires the acting user id.", nameof(actorUserId));
        }

        if (actorType != AuditActorType.User && actorUserId is not null)
        {
            throw new ArgumentException(
                "Only a user actor has an actor user id.", nameof(actorUserId));
        }

        Id = id;
        OccurredAtUtc = occurredAtUtc;
        BuildingId = buildingId;
        ActorType = actorType;
        ActorUserId = actorUserId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        CorrelationId = correlationId is { Length: > MaxCorrelationIdLength }
            ? correlationId[..MaxCorrelationIdLength]
            : correlationId;
        MetadataJson = metadataJson;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>NULL only for events with no building (e.g. a failed login).</summary>
    public Guid? BuildingId { get; private set; }

    public AuditActorType ActorType { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public AuditAction Action { get; private set; }

    public AuditTargetType TargetType { get; private set; }

    public Guid? TargetId { get; private set; }

    /// <summary>The HTTP request trace id when the event started from a request.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Small, allowlisted JSON built only by <c>AuditMetadata</c>.</summary>
    public string? MetadataJson { get; private set; }
}
