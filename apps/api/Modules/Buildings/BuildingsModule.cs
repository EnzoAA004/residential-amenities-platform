using ResidentialAmenities.Api.Modules.Buildings.Application;

namespace ResidentialAmenities.Api.Modules.Buildings;

public static class BuildingsModule
{
    public static IServiceCollection AddBuildingsModule(
        this IServiceCollection services)
    {
        services.AddScoped<
            IBuildingMembershipAuthorizer,
            BuildingMembershipAuthorizer>();

        return services;
    }
}
