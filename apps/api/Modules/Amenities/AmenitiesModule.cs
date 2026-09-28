using ResidentialAmenities.Api.Modules.Amenities.Application;

namespace ResidentialAmenities.Api.Modules.Amenities;

public static class AmenitiesModule
{
    public static IServiceCollection AddAmenitiesModule(
        this IServiceCollection services)
    {
        services.AddScoped<IAmenityAdminContract, AmenityAdminService>();

        return services;
    }
}
