using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Endpoints;
using ResidentialAmenities.Api.Infrastructure.Errors;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Administration;
using ResidentialAmenities.Api.Modules.Amenities;
using ResidentialAmenities.Api.Modules.Audit;
using ResidentialAmenities.Api.Modules.Buildings;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Media;
using ResidentialAmenities.Api.Modules.Messaging;
using ResidentialAmenities.Api.Modules.Notifications;
using ResidentialAmenities.Api.Modules.Payments;
using ResidentialAmenities.Api.Modules.Pricing;
using ResidentialAmenities.Api.Modules.Reporting;
using ResidentialAmenities.Api.Modules.Reports;
using ResidentialAmenities.Api.Modules.Reservations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var dataProtectionBlobUri = builder.Configuration["DataProtection:BlobUri"];
if (!string.IsNullOrWhiteSpace(dataProtectionBlobUri))
{
    builder.Services
        .AddDataProtection()
        .SetApplicationName("ResidentialAmenities")
        .PersistKeysToAzureBlobStorage(
            new Uri(dataProtectionBlobUri),
            new DefaultAzureCredential());
}

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
    .AddIdentityModule(builder.Configuration)
    .AddBuildingsModule()
    .AddAmenitiesModule()
    .AddReservationsModule(builder.Configuration)
    .AddPricingModule()
    .AddPaymentsModule(builder.Configuration)
    .AddAdministrationModule()
    .AddAuditModule()
    .AddNotificationsModule()
    .AddMessagingModule()
    .AddMediaModule(builder.Configuration)
    .AddReportingModule();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseCors("Client");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    await app.Services.SeedDevelopmentDataAsync();
    app.MapOpenApi();
}

if (builder.Configuration.GetValue<bool>("Demo:Enabled") &&
    (app.Environment.IsDevelopment() || app.Environment.IsStaging()))
{
    await app.Services.SeedDemoDataAsync();
}

if (app.Environment.IsStaging())
{
    await app.Services.SeedStagingAdministratorAsync(builder.Configuration);
}

app.MapIdentityEndpoints();
app.MapDemoEndpoints();
app.MapInvitationEndpoints();
app.MapWebAuthnEndpoints();
app.MapAmenityEndpoints();
app.MapPricingEndpoints();
app.MapReservationEndpoints();
app.MapEventSlotEndpoints();
app.MapReservationEntryPointEndpoints();
app.MapPaymentEndpoints();
app.MapAuditEndpoints();
app.MapNotificationEndpoints();
app.MapMessagingEndpoints();
app.MapMediaEndpoints();
app.MapReportsEndpoints();
app.MapAdminEndpoints();
app.MapHealthEndpoints();
app.MapGet("/", () => Results.Redirect("/health"));

app.Run();

public partial class Program;
