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
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Amenities;

[Collection(DevelopmentSeedCollection.Name)]
public sealed class AmenityEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("environment", "Development"));

    private string _residentEmail = string.Empty;
    private string _adminEmail = string.Empty;
    private Guid _otherBuildingId;
    private Guid _otherBuildingAmenityId;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _residentEmail = $"resident-{Guid.NewGuid():N}@example.test";
        _adminEmail = $"admin-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var resident = new UserAccount(
            Guid.NewGuid(),
            _residentEmail,
            "Resident Test");

        Assert.True(
            (await userManager.CreateAsync(resident, Password)).Succeeded);
        Assert.True(
            (await userManager.AddToRoleAsync(
                resident,
                ApplicationRoles.Resident)).Succeeded);

        var unit = await dbContext.Units
            .SingleAsync(unit => unit.Label == "1A", cancellationToken);

        dbContext.ResidentMemberships.Add(
            new ResidentMembership(
                Guid.NewGuid(),
                unit.BuildingId,
                unit.Id,
                resident.Id,
                DateTimeOffset.UtcNow));

        var admin = new UserAccount(
            Guid.NewGuid(),
            _adminEmail,
            "Administrator Test");

        Assert.True((await userManager.CreateAsync(admin, Password)).Succeeded);
        Assert.True(
            (await userManager.AddToRolesAsync(
                admin,
                [ApplicationRoles.Administrator, ApplicationRoles.Resident]))
                .Succeeded);

        var otherBuilding = new Building(
            Guid.NewGuid(),
            "Other Building",
            "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(otherBuilding);
        _otherBuildingId = otherBuilding.Id;

        var otherAmenity = new Amenity(
            Guid.NewGuid(),
            otherBuilding.Id,
            "Other SUM",
            AmenityKind.Sum,
            allowsSharedUse: true,
            allowsExclusiveUse: true);
        otherAmenity.AddAvailabilityWindow(
            Guid.NewGuid(),
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(22, 0));
        dbContext.Amenities.Add(otherAmenity);
        _otherBuildingAmenityId = otherAmenity.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ListAmenities_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/buildings/{DevelopmentDataSeeder.PilotBuildingId}/amenities",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListAmenities_ResidentOfBuilding_ReturnsSeededAmenities()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/buildings/{DevelopmentDataSeeder.PilotBuildingId}/amenities",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Contains("SUM", body);
        Assert.Contains("Pool", body);
        Assert.Contains("Barbecue", body);
    }

    [Fact]
    public async Task ListAmenities_ResidentWithoutMembership_Returns403()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/buildings/{_otherBuildingId}/amenities",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListAmenities_Administrator_CanAccessAnyBuilding()
    {
        using var client = await CreateAuthenticatedClientAsync(_adminEmail);

        var response = await client.GetAsync(
            $"/api/buildings/{_otherBuildingId}/amenities",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Availability_UnknownAmenity_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/amenities/{Guid.NewGuid()}/availability" +
            "?fromUtc=2026-10-05T00:00:00Z&toUtc=2026-10-06T00:00:00Z",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Availability_ResidentWithoutBuildingAccess_Returns403()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/amenities/{_otherBuildingAmenityId}/availability" +
            "?fromUtc=2026-10-05T00:00:00Z&toUtc=2026-10-06T00:00:00Z",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Availability_InvalidRange_Returns400()
    {
        using var client = await CreateAuthenticatedClientAsync(_adminEmail);

        var response = await client.GetAsync(
            $"/api/amenities/{_otherBuildingAmenityId}/availability" +
            "?fromUtc=2026-10-06T00:00:00Z&toUtc=2026-10-05T00:00:00Z",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Availability_ForPilotSum_ReturnsOpenIntervals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sum = await dbContext.Amenities.SingleAsync(
            amenity =>
                amenity.BuildingId == DevelopmentDataSeeder.PilotBuildingId &&
                amenity.Name == "SUM",
            cancellationToken);

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/amenities/{sum.Id}/availability" +
            "?fromUtc=2026-10-05T00:00:00Z&toUtc=2026-10-06T00:00:00Z",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var intervals = await response.Content
            .ReadFromJsonAsync<List<AvailabilityIntervalResponse>>(
                cancellationToken);

        Assert.NotNull(intervals);
        Assert.NotEmpty(intervals);
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

    private sealed record AvailabilityIntervalResponse(
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc);
}
