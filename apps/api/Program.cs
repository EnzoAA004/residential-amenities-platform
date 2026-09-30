using ResidentialAmenities.Api.Endpoints;
using ResidentialAmenities.Api.Infrastructure.Errors;
using ResidentialAmenities.Api.Infrastructure.Persistence;
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
            policy
                .WithOrigins(allowedOrigins)
                .AllowCredentials();
        }

        policy
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddIdentityModule()
    .AddBuildingsModule()
    .AddAmenitiesModule()
    .AddReservationsModule(builder.Configuration)
    .AddPricingModule()
    .AddPaymentsModule(builder.Configuration)
    .AddAdministrationModule()
    .AddAuditModule()
    .AddNotificationsModule()
    .AddMessagingModule()
    .AddReportingModule();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("Client");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    await app.Services.SeedDevelopmentDataAsync();
    app.MapOpenApi();
}

app.MapIdentityEndpoints();
app.MapAmenityEndpoints();
app.MapPricingEndpoints();
app.MapReservationEndpoints();
app.MapEventSlotEndpoints();
app.MapReservationEntryPointEndpoints();
app.MapPaymentEndpoints();
app.MapAuditEndpoints();
app.MapNotificationEndpoints();
app.MapAdminEndpoints();
app.MapHealthEndpoints();
app.MapGet("/", () => Results.Redirect("/health"));

app.Run();

public partial class Program;
