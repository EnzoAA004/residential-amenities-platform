using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Modules.Reservations.Application;

namespace ResidentialAmenities.Api.Modules.Reservations.Infrastructure;

/// <summary>
/// Periodically calls <see cref="ReservationExpirationService"/> so expired
/// holds are released automatically (RF-012), without any manual/admin
/// trigger. A fresh DI scope is created per tick because
/// <c>AppDbContext</c> is scoped while this hosted service is a singleton.
/// One tick's failure is logged and never crashes the host or skips future
/// ticks.
/// </summary>
public sealed class ReservationExpirationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationExpirationOptions> options,
    ILogger<ReservationExpirationHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var expirationService = scope.ServiceProvider
                    .GetRequiredService<ReservationExpirationService>();

                var expiredCount = await expirationService.ExpirePastHoldsAsync(
                    stoppingToken);

                if (expiredCount > 0)
                {
                    logger.LogInformation(
                        "Expired {Count} reservation hold(s).",
                        expiredCount);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(
                    error,
                    "Reservation hold expiration tick failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
