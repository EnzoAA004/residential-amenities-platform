using ResidentialAmenities.Api.Modules.Audit.Application;

namespace ResidentialAmenities.Api.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(
        this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditRecorder, AuditRecorder>();

        return services;
    }
}
