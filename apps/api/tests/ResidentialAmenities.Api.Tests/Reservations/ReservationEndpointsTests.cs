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
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Reservations;

[Collection(DevelopmentSeedCollection.Name)]
public sealed class ReservationEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("environment", "Development"));

    private string _residentEmail = string.Empty;
    private string _secondResidentEmail = string.Empty;
    private Guid _otherBuildingId;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _residentEmail = $"resident-{Guid.NewGuid():N}@example.test";
        _secondResidentEmail = $"resident2-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit1A = await dbContext.Units
            .SingleAsync(unit => unit.Label == "1A", cancellationToken);
        var unit1B = await dbContext.Units
            .SingleAsync(unit => unit.Label == "1B", cancellationToken);

        var resident = new UserAccount(Guid.NewGuid(), _residentEmail, "Resident One");
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

        var secondResident =
            new UserAccount(Guid.NewGuid(), _secondResidentEmail, "Resident Two");
        Assert.True(
            (await userManager.CreateAsync(secondResident, Password)).Succeeded);
        Assert.True(
            (await userManager.AddToRoleAsync(
                secondResident,
                ApplicationRoles.Resident)).Succeeded);
        dbContext.ResidentMemberships.Add(
            new ResidentMembership(
                Guid.NewGuid(),
                unit1B.BuildingId,
                unit1B.Id,
                secondResident.Id,
                DateTimeOffset.UtcNow));

        var otherBuilding = new Building(
            Guid.NewGuid(),
            "Other Reservations Building",
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
    public async Task Create_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(new DateTime(2027, 3, 1, 10, 0, 0)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ResidentWithoutMembershipInTargetBuilding_Returns403()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var request = BuildRequest(
            new DateTime(2027, 3, 1, 10, 0, 0),
            buildingId: _otherBuildingId);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_SharedLeisure_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(new DateTime(2027, 3, 2, 10, 0, 0)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal("SharedLeisure", body.UseType);
        // Every reservation starts as a payment hold (issue #23, RB-009):
        // there is still no payment step to confirm it immediately.
        Assert.Equal("Pending", body.Status);
        Assert.Equal(5_000m, body.TotalAmount);
        Assert.Equal("ARS", body.Currency);
    }

    [Fact]
    public async Task Create_ExpiresAtUtc_DerivesFromConfiguredHoldDuration()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Development");
                builder.UseSetting("Reservations:Hold:DurationMinutes", "1440");
            });

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email = _residentEmail, password = Password },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(new DateTime(2027, 3, 2, 11, 0, 0)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(TimeSpan.FromHours(24), body.ExpiresAtUtc - body.CreatedAtUtc);
    }

    [Fact]
    public async Task Create_SecondCompatibleSharedLeisure_Succeeds()
    {
        var start = new DateTime(2027, 3, 3, 10, 0, 0);

        using var firstClient = await CreateAuthenticatedClientAsync(_residentEmail);
        var first = await firstClient.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(start),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var secondClient =
            await CreateAuthenticatedClientAsync(_secondResidentEmail);
        var second = await secondClient.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(start),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Create_ExclusiveOverExistingShared_ReturnsConflict()
    {
        var start = new DateTime(2027, 3, 4, 10, 0, 0);

        using var firstClient = await CreateAuthenticatedClientAsync(_residentEmail);
        var shared = await firstClient.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(start, useType: "SharedLeisure"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, shared.StatusCode);

        using var secondClient =
            await CreateAuthenticatedClientAsync(_secondResidentEmail);
        var exclusive = await secondClient.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(start, useType: "ExclusiveLeisure"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, exclusive.StatusCode);
    }

    [Fact]
    public async Task Create_SharedOverExistingExclusive_ReturnsConflict()
    {
        var start = new DateTime(2027, 3, 5, 10, 0, 0);

        using var firstClient = await CreateAuthenticatedClientAsync(_residentEmail);
        var exclusive = await firstClient.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(start, useType: "ExclusiveLeisure"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, exclusive.StatusCode);

        using var secondClient =
            await CreateAuthenticatedClientAsync(_secondResidentEmail);
        var shared = await secondClient.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(start, useType: "SharedLeisure"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, shared.StatusCode);
    }

    [Fact]
    public async Task Create_ExclusiveOverExistingExclusive_ReturnsConflict()
    {
        var start = new DateTime(2027, 3, 6, 10, 0, 0);

        using var firstClient = await CreateAuthenticatedClientAsync(_residentEmail);
        var first = await firstClient.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(start, useType: "ExclusiveLeisure"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var secondClient =
            await CreateAuthenticatedClientAsync(_secondResidentEmail);
        var second = await secondClient.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(start, useType: "ExclusiveLeisure"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_OutsideConfiguredAvailability_ReturnsUnprocessable()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        // 02:00-03:00 local (America/Argentina/Buenos_Aires, UTC-3) is
        // outside the pilot's seeded 09:00-22:00 daily window.
        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(new DateTime(2027, 3, 7, 2, 0, 0)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuringMaintenancePeriod_ReturnsUnprocessable()
    {
        var start = new DateTime(2027, 3, 8, 10, 0, 0);
        var startUtc = new DateTimeOffset(start, TimeSpan.FromHours(-3));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext =
                scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Inserted directly (not through a loaded Amenity's tracked
            // collection) so this only ever issues an INSERT for the new
            // period, never touching the shared pilot Amenity row.
            dbContext.AmenityUnavailablePeriods.Add(new AmenityUnavailablePeriod(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotSumId,
                startUtc.ToUniversalTime(),
                startUtc.AddHours(1).ToUniversalTime(),
                "Scheduled maintenance"));

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildRequest(start),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_IgnoresClientSuppliedPrice_UsesServerQuote()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var start = new DateTimeOffset(
            new DateTime(2027, 3, 9, 10, 0, 0),
            TimeSpan.FromHours(-3));

        // Extra "totalAmount"/"price" fields a malicious client might send
        // are simply unknown JSON members to the server's request DTO.
        var payload = new
        {
            buildingId = DevelopmentDataSeeder.PilotBuildingId,
            amenityId = DevelopmentDataSeeder.PilotSumId,
            useType = "SharedLeisure",
            startsAtUtc = start,
            endsAtUtc = start.AddHours(1),
            totalAmount = 1m,
            price = 1m
        };

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            payload,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(5_000m, body.TotalAmount);
    }

    [Fact]
    public async Task ChangingPriceRuleLater_DoesNotAlterExistingReservationSnapshot()
    {
        // Uses a dedicated amenity/rule rather than the shared pilot SUM
        // rule, so superseding it here cannot affect any other test that
        // relies on the pilot's SharedLeisure price staying at 5,000 ARS.
        var amenityId = Guid.NewGuid();
        var effectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext =
                scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var amenity = new Amenity(
                amenityId,
                DevelopmentDataSeeder.PilotBuildingId,
                $"Snapshot Test Amenity {amenityId:N}",
                AmenityKind.Other,
                allowsSharedUse: true,
                allowsExclusiveUse: true);

            for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
            {
                amenity.AddAvailabilityWindow(
                    Guid.NewGuid(),
                    day,
                    new TimeOnly(9, 0),
                    new TimeOnly(22, 0));
            }

            dbContext.Amenities.Add(amenity);

            dbContext.PriceRules.Add(new PriceRule(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotBuildingId,
                amenityId,
                PriceComponentType.Base,
                ReservationUseType.SharedLeisure,
                "ARS",
                4_000m,
                effectiveFrom,
                null));

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var start = new DateTimeOffset(
            new DateTime(2027, 3, 10, 10, 0, 0),
            TimeSpan.FromHours(-3));

        var created = await client.PostAsJsonAsync(
            "/api/reservations",
            new
            {
                buildingId = DevelopmentDataSeeder.PilotBuildingId,
                amenityId,
                useType = "SharedLeisure",
                startsAtUtc = start,
                endsAtUtc = start.AddHours(1)
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var reservation = await created.Content
            .ReadFromJsonAsync<ReservationResponse>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(reservation);
        Assert.Equal(4_000m, reservation.TotalAmount);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext =
                scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var currentRule = await dbContext.PriceRules.SingleAsync(
                rule =>
                    rule.BuildingId == DevelopmentDataSeeder.PilotBuildingId &&
                    rule.AmenityId == amenityId &&
                    rule.ComponentType == PriceComponentType.Base &&
                    rule.UseType == ReservationUseType.SharedLeisure,
                TestContext.Current.CancellationToken);

            var now = DateTimeOffset.UtcNow;
            currentRule.Supersede(now);

            dbContext.PriceRules.Add(new PriceRule(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotBuildingId,
                amenityId,
                PriceComponentType.Base,
                ReservationUseType.SharedLeisure,
                "ARS",
                9_999m,
                now,
                null));

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var refetched = await client.GetAsync(
            $"/api/reservations/{reservation.Id}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, refetched.StatusCode);

        var refetchedBody = await refetched.Content
            .ReadFromJsonAsync<ReservationResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(refetchedBody);
        Assert.Equal(4_000m, refetchedBody.TotalAmount);
    }

    private static object BuildRequest(
        DateTime localStart,
        string useType = "SharedLeisure",
        Guid? buildingId = null)
    {
        var startUtc = new DateTimeOffset(localStart, TimeSpan.FromHours(-3));

        return new
        {
            buildingId = buildingId ?? DevelopmentDataSeeder.PilotBuildingId,
            amenityId = DevelopmentDataSeeder.PilotSumId,
            useType,
            startsAtUtc = startUtc,
            endsAtUtc = startUtc.AddHours(1)
        };
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

    private sealed record ReservationResponse(
        Guid Id,
        Guid BuildingId,
        string UseType,
        string Status,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        List<ReservationResourceResponse> Resources,
        List<ReservationPriceLineResponse> PriceLines,
        string Currency,
        decimal TotalAmount);

    private sealed record ReservationResourceResponse(Guid AmenityId, bool IsExclusive);

    private sealed record ReservationPriceLineResponse(
        Guid AmenityId,
        string ComponentType,
        string Currency,
        decimal Amount);
}
