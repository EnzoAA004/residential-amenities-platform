namespace ResidentialAmenities.Api.Modules.Audit.Domain;

/// <summary>Who performed an audited action. Not every action has a user account.</summary>
public enum AuditActorType
{
    /// <summary>An authenticated person. <c>ActorUserId</c> is required.</summary>
    User = 0,

    /// <summary>The application itself (background job, trusted internal contract, or an unauthenticated attempt). <c>ActorUserId</c> is NULL.</summary>
    System = 1,

    /// <summary>An external provider (e.g. Mercado Pago). <c>ActorUserId</c> is NULL.</summary>
    ExternalProvider = 2
}
