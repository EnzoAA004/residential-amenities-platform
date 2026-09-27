namespace ResidentialAmenities.Api.Modules.Identity;

public static class ApplicationRoles
{
    public const string Resident = "Resident";
    public const string Administrator = "Administrator";

    public static readonly Guid ResidentId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static readonly Guid AdministratorId =
        Guid.Parse("10000000-0000-0000-0000-000000000002");

    // Role seed values must remain deterministic. ASP.NET Core Identity
    // otherwise generates a new ConcurrencyStamp for each model build,
    // which makes EF Core report pending model changes on every CI run.
    public const string ResidentConcurrencyStamp =
        "aeaa54e8-bc49-4705-ae9e-2954df445a02";

    public const string AdministratorConcurrencyStamp =
        "7c7a929d-4171-49ae-8ca2-06d23891e972";
}

public static class AuthorizationPolicies
{
    public const string ResidentAccess = "ResidentAccess";
    public const string Administrator = "Administrator";
}
