namespace ResidentialAmenities.Api.Modules.Identity;

public static class ApplicationRoles
{
    public const string Resident = "Resident";
    public const string Administrator = "Administrator";

    public static readonly Guid ResidentId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static readonly Guid AdministratorId =
        Guid.Parse("10000000-0000-0000-0000-000000000002");
}

public static class AuthorizationPolicies
{
    public const string ResidentAccess = "ResidentAccess";
    public const string Administrator = "Administrator";
}
