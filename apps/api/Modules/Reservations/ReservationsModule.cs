using ResidentialAmenities.Api.Modules.Reservations.Application;

namespace ResidentialAmenities.Api.Modules.Reservations;

public static class ReservationsModule
{
    public static IServiceCollection AddReservationsModule(
        this IServiceCollection services)
    {
        services.AddScoped<ReservationCreationService>();

        return services;
    }
}
