using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalClient", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200", "http://localhost:8100")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var connectionString = builder.Configuration.GetConnectionString("Postgres");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Missing PostgreSQL connection string. Set ConnectionStrings__Postgres.");
}

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

var app = builder.Build();

app.UseCors("LocalClient");

app.MapGet("/api/health", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
{
    try
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return Results.Ok(new
        {
            status = "ok",
            database = Convert.ToInt32(result) == 1 ? "ok" : "unexpected",
            utc = DateTimeOffset.UtcNow
        });
    }
    catch (Exception exception)
    {
        return Results.Json(
            new
            {
                status = "degraded",
                database = "unavailable",
                error = exception.GetType().Name,
                utc = DateTimeOffset.UtcNow
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/", () => Results.Redirect("/api/health"));

app.Run();
