using ResidentialAmenities.Api.Modules.Pricing.Application;

namespace ResidentialAmenities.Api.Modules.Pricing;

public static class PricingModule
{
    public static IServiceCollection AddPricingModule(
        this IServiceCollection services)
    {
        services.AddScoped<IPricingAdminContract, PricingAdminService>();

        return services;
    }
}
