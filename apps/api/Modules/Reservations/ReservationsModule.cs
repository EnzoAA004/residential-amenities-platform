using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Modules.Reservations.Application;
using ResidentialAmenities.Api.Modules.Reservations.Infrastructure;

namespace ResidentialAmenities.Api.Modules.Reservations;

public static class ReservationsModule
{
    public static IServiceCollection AddReservationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ReservationCreationService>();
        services.AddScoped<ReservationExpirationService>();

        services
            .AddOptions<ReservationHoldOptions>()
            .Bind(configuration.GetSection(ReservationHoldOptions.SectionName));

        services
            .AddOptions<ReservationExpirationOptions>()
            .Bind(configuration.GetSection(ReservationExpirationOptions.SectionName));

        services.AddHostedService<ReservationExpirationHostedService>();

        return services;
    }
}
