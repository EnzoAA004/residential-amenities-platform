using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Modules.Payments.Application;
using ResidentialAmenities.Api.Modules.Payments.Infrastructure;

namespace ResidentialAmenities.Api.Modules.Payments;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<MercadoPagoOptions>()
            .Bind(configuration.GetSection(MercadoPagoOptions.SectionName));

        services.AddHttpClient<IMercadoPagoClient, MercadoPagoHttpClient>(
            (provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<MercadoPagoOptions>>().Value;
                client.BaseAddress = new Uri(settings.ApiBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            });

        services.AddScoped<PaymentCreationService>();
        services.AddScoped<PaymentReconciliationService>();

        return services;
    }
}
