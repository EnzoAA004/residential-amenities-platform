using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Pricing;

[Collection(DevelopmentSeedCollection.Name)]
public sealed class PricingEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("environment", "Development"));

    private string _residentEmail = string.Empty;
    private string _adminEmail = string.Empty;
    private Guid _otherBuildingId;

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
            "Other Pricing Building",
            "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(otherBuilding);
        _otherBuildingId = otherBuilding.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task GetQuote_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            QuoteUrl(
                DevelopmentDataSeeder.PilotBuildingId,
                DevelopmentDataSeeder.PilotSumId,
                "SharedLeisure"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetQuote_ResidentWithoutMembership_Returns403()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            QuoteUrl(
                _otherBuildingId,
                DevelopmentDataSeeder.PilotSumId,
                "SharedLeisure"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetQuote_SharedLeisureBaseOnly_ReturnsPilotAmount()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            QuoteUrl(
                DevelopmentDataSeeder.PilotBuildingId,
                DevelopmentDataSeeder.PilotSumId,
                "SharedLeisure"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var quote = await response.Content.ReadFromJsonAsync<QuoteResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(quote);
        Assert.Equal(5_000m, quote.TotalAmount);
        Assert.Equal("ARS", quote.Currency);
        Assert.Single(quote.Lines);
    }

    [Fact]
    public async Task GetQuote_EventWithAddOns_SumsComponents()
    {
        using var client = await CreateAuthenticatedClientAsync(_adminEmail);

        var url =
            $"{QuoteUrl(DevelopmentDataSeeder.PilotBuildingId, DevelopmentDataSeeder.PilotSumId, "Event")}" +
            $"&addOnAmenityId={DevelopmentDataSeeder.PilotPoolId}" +
            $"&addOnAmenityId={DevelopmentDataSeeder.PilotBarbecueId}";

        var response = await client.GetAsync(
            url,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var quote = await response.Content.ReadFromJsonAsync<QuoteResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(quote);
        Assert.Equal(21_000m, quote.TotalAmount);
        Assert.Equal(3, quote.Lines.Count);
    }

    [Fact]
    public async Task GetQuote_UnknownAmenity_Returns422()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            QuoteUrl(
                DevelopmentDataSeeder.PilotBuildingId,
                Guid.NewGuid(),
                "SharedLeisure"),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            response.StatusCode);
    }

    private static string QuoteUrl(
        Guid buildingId,
        Guid amenityId,
        string useType) =>
        $"/api/pricing/quote?buildingId={buildingId}&amenityId={amenityId}" +
        $"&useType={useType}";

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

    private sealed record QuoteResponse(
        string Currency,
        decimal TotalAmount,
        DateTimeOffset QuotedAtUtc,
        List<QuoteLineResponse> Lines);

    private sealed record QuoteLineResponse(
        Guid PriceRuleId,
        Guid AmenityId,
        PriceComponentType ComponentType,
        string Currency,
        decimal Amount);
}
