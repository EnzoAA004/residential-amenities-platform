using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

/// <summary>
/// Issue #89: before confirming a SharedLeisure reservation, a resident must
/// see which units already hold an overlapping booking for the same
/// amenity — DEC-014/OQ-009 removed any capacity cap, so this is purely
/// informational (never a rejection). The response must expose unit display
/// labels only, never a name, email, user id, membership id or phone.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class SharedOccupancyEndpointTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

    private string _residentEmail = string.Empty;
    private Guid _buildingId;
    private Guid _amenityId;
    private Guid _membershipAId;
    private Guid _membershipBId;
    private Guid _membershipCId;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _residentEmail = $"occupancy-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var building = new Building(Guid.NewGuid(), "Occupancy Building", "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(building);
        _buildingId = building.Id;

        var unitA = new Unit(Guid.NewGuid(), building.Id, floor: 1, door: "1", label: "1A");
        var unitB = new Unit(Guid.NewGuid(), building.Id, floor: 1, door: "2", label: "1B");
        var unitC = new Unit(Guid.NewGuid(), building.Id, floor: 2, door: "1", label: "2A");
        dbContext.Units.AddRange(unitA, unitB, unitC);

        var amenity = new Amenity(
            Guid.NewGuid(), building.Id, "Pool", AmenityKind.Pool,
            allowsSharedUse: true, allowsExclusiveUse: false);
        dbContext.Amenities.Add(amenity);
        _amenityId = amenity.Id;

        var resident = new UserAccount(Guid.NewGuid(), _residentEmail, "Occupancy Resident");
        Assert.True((await userManager.CreateAsync(resident, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(resident, ApplicationRoles.Resident)).Succeeded);

        var nowUtc = DateTimeOffset.UtcNow;
        var membershipA = new ResidentMembership(Guid.NewGuid(), building.Id, unitA.Id, resident.Id, nowUtc);
        dbContext.ResidentMemberships.Add(membershipA);
        _membershipAId = membershipA.Id;

        // Two more residents (units B and C) so the occupancy list reflects
        // more than one distinct membership/unit.
        var residentB = new UserAccount(Guid.NewGuid(), $"occ-b-{Guid.NewGuid():N}@example.test", "Resident B");
        Assert.True((await userManager.CreateAsync(residentB, Password)).Succeeded);
        var membershipB = new ResidentMembership(Guid.NewGuid(), building.Id, unitB.Id, residentB.Id, nowUtc);
        dbContext.ResidentMemberships.Add(membershipB);
        _membershipBId = membershipB.Id;

        var residentC = new UserAccount(Guid.NewGuid(), $"occ-c-{Guid.NewGuid():N}@example.test", "Resident C");
        Assert.True((await userManager.CreateAsync(residentC, Password)).Succeeded);
        var membershipC = new ResidentMembership(Guid.NewGuid(), building.Id, unitC.Id, residentC.Id, nowUtc);
        dbContext.ResidentMemberships.Add(membershipC);
        _membershipCId = membershipC.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task NoOverlappingReservations_ReturnsEmptyList()
    {
        using var client = await LoginAsync(_residentEmail);
        var (startUtc, endUtc) = Window(DateTimeOffset.UtcNow.AddDays(1));

        var labels = await GetOccupancyAsync(client, startUtc, endUtc);

        Assert.Empty(labels);
    }

    [Fact]
    public async Task OverlappingSharedLeisureReservations_ListsDistinctUnitLabels_SortedAndWithoutPii()
    {
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var (startUtc, endUtc) = Window(start);

        await AddReservationAsync(_membershipBId, ReservationStatus.Confirmed, startUtc, endUtc);
        // Only partially overlapping — still counts.
        await AddReservationAsync(
            _membershipCId, ReservationStatus.Pending, startUtc.AddMinutes(30), endUtc.AddMinutes(30));

        using var client = await LoginAsync(_residentEmail);
        var response = await client.GetAsync(
            $"/api/reservations/shared-occupancy?buildingId={_buildingId}&amenityId={_amenityId}" +
            $"&startsAtUtc={Uri.EscapeDataString(startUtc.ToString("O"))}" +
            $"&endsAtUtc={Uri.EscapeDataString(endUtc.ToString("O"))}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("1B", raw);
        Assert.Contains("2A", raw);

        // Hard privacy requirement: never a name, email, user id or
        // membership id anywhere in the response.
        Assert.DoesNotContain("Resident B", raw);
        Assert.DoesNotContain("Resident C", raw);
        Assert.DoesNotContain("@example.test", raw);
        Assert.DoesNotContain(_membershipBId.ToString(), raw);
        Assert.DoesNotContain(_membershipCId.ToString(), raw);

        using var body = JsonDocument.Parse(raw);
        var labels = body.RootElement.GetProperty("unitLabels")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["1B", "2A"], labels);
    }

    [Fact]
    public async Task ExpiredOrCancelledReservations_AreExcluded()
    {
        var start = DateTimeOffset.UtcNow.AddDays(3);
        var (startUtc, endUtc) = Window(start);

        await AddReservationAsync(
            _membershipBId, ReservationStatus.Pending, startUtc, endUtc,
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        await AddReservationAsync(_membershipCId, ReservationStatus.Cancelled, startUtc, endUtc);

        var labels = await GetOccupancyAsync(await LoginAsync(_residentEmail), startUtc, endUtc);

        Assert.Empty(labels);
    }

    [Fact]
    public async Task NonOverlappingPeriod_IsExcluded()
    {
        var start = DateTimeOffset.UtcNow.AddDays(4);
        var (startUtc, endUtc) = Window(start);
        await AddReservationAsync(_membershipBId, ReservationStatus.Confirmed, startUtc, endUtc);

        // A different, non-overlapping hour the same day.
        var (otherStartUtc, otherEndUtc) = Window(start.AddHours(5));
        var labels = await GetOccupancyAsync(await LoginAsync(_residentEmail), otherStartUtc, otherEndUtc);

        Assert.Empty(labels);
    }

    [Fact]
    public async Task ResidentWithoutBuildingAccess_Returns403()
    {
        var outsider = $"occ-outsider-{Guid.NewGuid():N}@example.test";
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var outsiderUser = new UserAccount(Guid.NewGuid(), outsider, "Outsider");
        Assert.True((await userManager.CreateAsync(outsiderUser, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(outsiderUser, ApplicationRoles.Resident)).Succeeded);

        var (startUtc, endUtc) = Window(DateTimeOffset.UtcNow.AddDays(1));
        using var client = await LoginAsync(outsider);

        var response = await client.GetAsync(
            $"/api/reservations/shared-occupancy?buildingId={_buildingId}&amenityId={_amenityId}" +
            $"&startsAtUtc={Uri.EscapeDataString(startUtc.ToString("O"))}" +
            $"&endsAtUtc={Uri.EscapeDataString(endUtc.ToString("O"))}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnknownAmenity_Returns404()
    {
        var (startUtc, endUtc) = Window(DateTimeOffset.UtcNow.AddDays(1));
        using var client = await LoginAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/reservations/shared-occupancy?buildingId={_buildingId}&amenityId={Guid.NewGuid()}" +
            $"&startsAtUtc={Uri.EscapeDataString(startUtc.ToString("O"))}" +
            $"&endsAtUtc={Uri.EscapeDataString(endUtc.ToString("O"))}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<List<string>> GetOccupancyAsync(
        HttpClient client, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var response = await client.GetAsync(
            $"/api/reservations/shared-occupancy?buildingId={_buildingId}&amenityId={_amenityId}" +
            $"&startsAtUtc={Uri.EscapeDataString(startUtc.ToString("O"))}" +
            $"&endsAtUtc={Uri.EscapeDataString(endUtc.ToString("O"))}",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions, TestContext.Current.CancellationToken);

        return body.GetProperty("unitLabels").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    private async Task AddReservationAsync(
        Guid membershipId,
        ReservationStatus status,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        DateTimeOffset? expiresAtUtc = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nowUtc = DateTimeOffset.UtcNow;
        // Reservation requires expiresAtUtc > createdAtUtc even for a hold
        // that is meant to already be expired relative to "now" — back-date
        // createdAtUtc too whenever an explicit (past) expiresAtUtc is
        // given, so the domain invariant holds while the hold is still
        // expired by the time the test's request runs.
        var createdAtUtc = expiresAtUtc.HasValue ? expiresAtUtc.Value.AddMinutes(-1) : nowUtc;
        var reservation = new Reservation(
            Guid.NewGuid(),
            _buildingId,
            membershipId,
            ReservationUseType.SharedLeisure,
            startsAtUtc,
            endsAtUtc,
            createdAtUtc,
            expiresAtUtc ?? nowUtc.AddMinutes(30));

        reservation.AddResource(Guid.NewGuid(), _amenityId, isExclusive: false);

        if (status == ReservationStatus.Confirmed)
        {
            reservation.Confirm(nowUtc);
        }
        else if (status == ReservationStatus.Cancelled)
        {
            reservation.Cancel(nowUtc);
        }

        dbContext.Reservations.Add(reservation);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) Window(DateTimeOffset start) =>
        (start, start.AddHours(1));

    private async Task<HttpClient> LoginAsync(string email)
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
}
