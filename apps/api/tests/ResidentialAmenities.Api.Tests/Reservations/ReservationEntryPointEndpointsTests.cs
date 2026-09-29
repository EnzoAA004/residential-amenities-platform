using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Reservations;

[Collection(DevelopmentSeedCollection.Name)]
public sealed class ReservationEntryPointEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("environment", "Development"));

    private string _residentEmail = string.Empty;
    private string _otherBuildingToken = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _residentEmail = $"qr-resident-{Guid.NewGuid():N}@example.test";
        _otherBuildingToken = $"other-building-{Guid.NewGuid():N}";

        await using var scope = _factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit1A = await dbContext.Units
            .SingleAsync(unit => unit.Label == "1A", cancellationToken);

        var resident = new UserAccount(Guid.NewGuid(), _residentEmail, "QR Resident");
        Assert.True((await userManager.CreateAsync(resident, Password)).Succeeded);
        Assert.True(
            (await userManager.AddToRoleAsync(resident, ApplicationRoles.Resident))
                .Succeeded);
        dbContext.ResidentMemberships.Add(
            new ResidentMembership(
                Guid.NewGuid(),
                unit1A.BuildingId,
                unit1A.Id,
                resident.Id,
                DateTimeOffset.UtcNow));

        var otherBuilding = new Building(
            Guid.NewGuid(),
            "QR Other Building",
            "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(otherBuilding);

        var otherAmenity = new Amenity(
            Guid.NewGuid(),
            otherBuilding.Id,
            "Other QR SUM",
            AmenityKind.Sum,
            allowsSharedUse: true,
            allowsExclusiveUse: true);
        dbContext.Amenities.Add(otherAmenity);

        dbContext.ReservationEntryPoints.Add(new ReservationEntryPoint(
            Guid.NewGuid(),
            _otherBuildingToken,
            otherBuilding.Id,
            otherAmenity.Id,
            ReservationUseType.SharedLeisure,
            "Other QR"));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            "/api/reservation-entry-points/pilot-sum",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownToken_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            "/api/reservation-entry-points/does-not-exist",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_ResidentWithoutBuildingMembership_Returns403()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/reservation-entry-points/{_otherBuildingToken}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_AuthorizedResident_ReturnsSafeAmenityContext()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            "/api/reservation-entry-points/PILOT-SUM",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entryPoint = await response.Content
            .ReadFromJsonAsync<ReservationEntryPointResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(entryPoint);
        Assert.Equal("pilot-sum", entryPoint.Token);
        Assert.Equal(DevelopmentDataSeeder.PilotBuildingId, entryPoint.BuildingId);
        Assert.Equal(DevelopmentDataSeeder.PilotSumId, entryPoint.AmenityId);
        Assert.Equal("Pilot SUM", entryPoint.DisplayName);
        Assert.Equal("SUM", entryPoint.AmenityName);
        Assert.Equal("Sum", entryPoint.AmenityKind);
        Assert.True(entryPoint.AllowsSharedUse);
        Assert.True(entryPoint.AllowsExclusiveUse);
        Assert.Equal("SharedLeisure", entryPoint.SuggestedUseType);
    }

    [Fact]
    public async Task Get_InactiveEntryPoint_Returns404()
    {
        var token = $"inactive-{Guid.NewGuid():N}";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entryPoint = new ReservationEntryPoint(
                Guid.NewGuid(),
                token,
                DevelopmentDataSeeder.PilotBuildingId,
                DevelopmentDataSeeder.PilotSumId);
            entryPoint.Deactivate();
            dbContext.ReservationEntryPoints.Add(entryPoint);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/reservation-entry-points/{token}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_InactiveAmenity_Returns404()
    {
        var token = $"inactive-amenity-{Guid.NewGuid():N}";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var amenity = new Amenity(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotBuildingId,
                $"Inactive QR Amenity {Guid.NewGuid():N}",
                AmenityKind.Other,
                allowsSharedUse: true,
                allowsExclusiveUse: false);
            amenity.Deactivate();

            dbContext.Amenities.Add(amenity);
            dbContext.ReservationEntryPoints.Add(new ReservationEntryPoint(
                Guid.NewGuid(),
                token,
                DevelopmentDataSeeder.PilotBuildingId,
                amenity.Id));

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/reservation-entry-points/{token}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email, password = Password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return client;
    }

    private sealed record ReservationEntryPointResponse(
        string Token,
        Guid BuildingId,
        Guid AmenityId,
        string DisplayName,
        string AmenityName,
        string AmenityKind,
        bool AllowsSharedUse,
        bool AllowsExclusiveUse,
        string? SuggestedUseType);
}
