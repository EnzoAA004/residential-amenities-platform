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
        services.AddScoped<ReservationScheduleValidator>();
        services.AddScoped<IReservationAdminQuery, ReservationAdminQuery>();
        services.AddScoped<IResidentReservationQuery, ResidentReservationQuery>();
        services.AddScoped<IReservationAdminContract, ReservationAdminService>();
        services.AddScoped<IEventSlotAdminContract, EventSlotAdminService>();
        services.AddScoped<IReservationPaymentContract, ReservationPaymentContract>();

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
