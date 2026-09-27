using Npgsql;
using ResidentialAmenities.Api.Endpoints;
using ResidentialAmenities.Api.Infrastructure.Errors;
using ResidentialAmenities.Api.Modules.Administration;
using ResidentialAmenities.Api.Modules.Amenities;
using ResidentialAmenities.Api.Modules.Audit;
using ResidentialAmenities.Api.Modules.Buildings;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Messaging;
using ResidentialAmenities.Api.Modules.Notifications;
using ResidentialAmenities.Api.Modules.Payments;
using ResidentialAmenities.Api.Modules.Pricing;
using ResidentialAmenities.Api.Modules.Reporting;
using ResidentialAmenities.Api.Modules.Reservations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var allowedOrigins =
    builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>()
    ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Client", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }

        policy
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var connectionString =
    builder.Configuration.GetConnectionString("Postgres");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Missing PostgreSQL connection string. " +
        "Set ConnectionStrings__Postgres.");
}

builder.Services.AddSingleton(
    _ => NpgsqlDataSource.Create(connectionString));

builder.Services
    .AddIdentityModule()
    .AddBuildingsModule()
    .AddAmenitiesModule()
    .AddReservationsModule()
    .AddPricingModule()
    .AddPaymentsModule()
    .AddAdministrationModule()
    .AddAuditModule()
    .AddNotificationsModule()
    .AddMessagingModule()
    .AddReportingModule();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("Client");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapGet("/", () => Results.Redirect("/health"));

app.Run();
