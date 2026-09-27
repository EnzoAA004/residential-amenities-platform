using Npgsql;

namespace ResidentialAmenities.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        static async Task<IResult> CheckHealth(
            NpgsqlDataSource dataSource,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var connection =
                    await dataSource.OpenConnectionAsync(cancellationToken);

                await using var command =
                    new NpgsqlCommand("SELECT 1", connection);

                var result =
                    await command.ExecuteScalarAsync(cancellationToken);

                var databaseHealthy = Convert.ToInt32(result) == 1;

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
                            database = "unexpected",
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
