using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;

namespace ResidentialAmenities.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        static async Task<IResult> CheckHealth(
            AppDbContext dbContext,
            CancellationToken cancellationToken)
        {
            try
            {
                var databaseHealthy =
                    await dbContext.Database.CanConnectAsync(
                        cancellationToken);

                return databaseHealthy
                    ? Results.Ok(new
                    {
                        status = "ok",
                        database = "ok",
                        utc = DateTimeOffset.UtcNow
                    })
                    : Results.Json(
                        new
                        {
                            status = "degraded",
                            database = "unavailable",
                            utc = DateTimeOffset.UtcNow
                        },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch
            {
                return Results.Json(
                    new
                    {
                        status = "degraded",
                        database = "unavailable",
                        utc = DateTimeOffset.UtcNow
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }

        endpoints.MapGet("/health", CheckHealth)
            .WithName("Health")
            .WithTags("Operations");

        // Kept temporarily for compatibility with the spike client.
        endpoints.MapGet("/api/health", CheckHealth)
            .WithName("ApiHealth")
            .WithTags("Operations");

        return endpoints;
    }
}
