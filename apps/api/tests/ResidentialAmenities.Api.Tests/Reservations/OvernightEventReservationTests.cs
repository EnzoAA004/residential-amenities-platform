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
/// DEC-014/OQ-002: the night Event slot is 20:00 -&gt; 03:00 the next day, a
/// real overnight slot. These tests use a dedicated building/amenity/slot
/// fixture (not the pilot seed, whose Event slots predate this decision)
/// so the overnight-specific plumbing — <see cref="EventSlotDefinition"/>'s
/// <c>IsOvernight</c> flag and
/// <c>ReservationScheduleValidator.EnsureValidEventSlotAsync</c>'s
/// day-delta check — is exercised in isolation.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class OvernightEventReservationTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly TimeSpan BuildingOffset = TimeSpan.FromHours(-3);

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

    private string _residentEmail = string.Empty;
    private string _secondResidentEmail = string.Empty;
    private Guid _buildingId;
    private Guid _sumId;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _residentEmail = $"overnight-resident-{Guid.NewGuid():N}@example.test";
        _secondResidentEmail = $"overnight-resident2-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var building = new Building(
            Guid.NewGuid(), "Overnight Building", "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(building);
        _buildingId = building.Id;

        var unit = new Unit(Guid.NewGuid(), building.Id, floor: 1, door: "1", label: "O1");
        dbContext.Units.Add(unit);
        var secondUnit = new Unit(Guid.NewGuid(), building.Id, floor: 1, door: "2", label: "O2");
        dbContext.Units.Add(secondUnit);

        var sum = new Amenity(
            Guid.NewGuid(),
            building.Id,
            "Overnight SUM",
            AmenityKind.Sum,
            allowsSharedUse: true,
            allowsExclusiveUse: true);
        dbContext.Amenities.Add(sum);
        _sumId = sum.Id;

        // Open every day of the week, all day — see the same rationale in
        // ZeroCostLeisureReservationTests: an amenity with no
        // AmenityAvailabilityWindow rows has zero open intervals.
        foreach (var dayOfWeek in Enum.GetValues<DayOfWeek>())
        {
            dbContext.AmenityAvailabilityWindows.Add(new AmenityAvailabilityWindow(
                Guid.NewGuid(), sum.Id, dayOfWeek, new TimeOnly(0, 0), TimeOnly.MaxValue));
        }

        dbContext.EventSlotDefinitions.Add(new EventSlotDefinition(
            Guid.NewGuid(),
            building.Id,
            "Night",
            new TimeOnly(20, 0),
            new TimeOnly(3, 0),
            isOvernight: true));

        var nowUtc = DateTimeOffset.UtcNow;

        dbContext.PriceRules.Add(new PriceRule(
            Guid.NewGuid(),
            building.Id,
            sum.Id,
            PriceComponentType.Base,
            ReservationUseType.Event,
            "ARS",
            5_000m,
            nowUtc.AddDays(-1),
            null));

        var resident = new UserAccount(Guid.NewGuid(), _residentEmail, "Overnight Resident");
        Assert.True((await userManager.CreateAsync(resident, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(resident, ApplicationRoles.Resident)).Succeeded);
        dbContext.ResidentMemberships.Add(new ResidentMembership(
            Guid.NewGuid(), building.Id, unit.Id, resident.Id, nowUtc));

        var secondResident = new UserAccount(Guid.NewGuid(), _secondResidentEmail, "Overnight Resident Two");
        Assert.True((await userManager.CreateAsync(secondResident, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(secondResident, ApplicationRoles.Resident)).Succeeded);
        dbContext.ResidentMemberships.Add(new ResidentMembership(
            Guid.NewGuid(), building.Id, secondUnit.Id, secondResident.Id, nowUtc));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Create_OvernightEventReservation_Succeeds_AndSpansCorrectUtcInstants()
    {
        using var client = await LoginAsync(_residentEmail);
        var cancellationToken = TestContext.Current.CancellationToken;

        var date = new DateOnly(2027, 6, 11); // an arbitrary future Friday-equivalent date
        var (startUtc, endUtc) = ToUtc(date, new TimeOnly(20, 0), new TimeOnly(3, 0));

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            new
            {
                buildingId = _buildingId,
                amenityId = _sumId,
                useType = "Event",
                startsAtUtc = startUtc,
                endsAtUtc = endUtc
            },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var returnedStart = body.RootElement.GetProperty("startsAtUtc").GetDateTimeOffset();
        var returnedEnd = body.RootElement.GetProperty("endsAtUtc").GetDateTimeOffset();

        Assert.Equal(startUtc, returnedStart);
        Assert.Equal(endUtc, returnedEnd);
        // 20:00 - 3:00 building-local = exactly 7 hours, regardless of the
        // UTC calendar-day boundary crossed in between.
        Assert.Equal(TimeSpan.FromHours(7), returnedEnd - returnedStart);
    }

    [Fact]
    public async Task Create_OverlappingOvernightEvent_OnSameSum_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var date = new DateOnly(2027, 6, 18);
        var (startUtc, endUtc) = ToUtc(date, new TimeOnly(20, 0), new TimeOnly(3, 0));

        using var firstClient = await LoginAsync(_residentEmail);
        var first = await firstClient.PostAsJsonAsync(
            "/api/reservations",
            new { buildingId = _buildingId, amenityId = _sumId, useType = "Event", startsAtUtc = startUtc, endsAtUtc = endUtc },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // A second Event overlapping only the early-morning tail (01:00-04:00
        // the next day) must still conflict — the overnight hold has to
        // block resources on both sides of the midnight boundary.
        var (overlapStartUtc, overlapEndUtc) = ToUtc(
            date.AddDays(1), new TimeOnly(1, 0), new TimeOnly(4, 0));

        using var secondClient = await LoginAsync(_secondResidentEmail);
        var second = await secondClient.PostAsJsonAsync(
            "/api/reservations",
            new { buildingId = _buildingId, amenityId = _sumId, useType = "Event", startsAtUtc = overlapStartUtc, endsAtUtc = overlapEndUtc },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_UnconfiguredOvernightRange_Returns422()
    {
        using var client = await LoginAsync(_residentEmail);
        var cancellationToken = TestContext.Current.CancellationToken;

        // 22:00 -> 02:00 crosses exactly one midnight (like the configured
        // Night slot) but does not match its exact boundaries — an
        // overnight *shape* is not enough, it must match a real slot.
        var date = new DateOnly(2027, 6, 25);
        var (startUtc, endUtc) = ToUtc(date, new TimeOnly(22, 0), new TimeOnly(2, 0));

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            new { buildingId = _buildingId, amenityId = _sumId, useType = "Event", startsAtUtc = startUtc, endsAtUtc = endUtc },
            cancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_RangeSpanningMoreThanOneMidnight_Returns422()
    {
        using var client = await LoginAsync(_residentEmail);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Two full days later — spans two midnight boundaries, which no
        // Event slot (overnight or not) may ever represent.
        var date = new DateOnly(2027, 7, 2);
        var (startUtc, endUtc) = ToUtc(date, new TimeOnly(20, 0), new TimeOnly(3, 0));
        endUtc = endUtc.AddDays(1);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            new { buildingId = _buildingId, amenityId = _sumId, useType = "Event", startsAtUtc = startUtc, endsAtUtc = endUtc },
            cancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) ToUtc(
        DateOnly startDate, TimeOnly startTime, TimeOnly endTime)
    {
        var endDate = endTime <= startTime ? startDate.AddDays(1) : startDate;

        var start = new DateTimeOffset(startDate.ToDateTime(startTime), BuildingOffset).ToUniversalTime();
        var end = new DateTimeOffset(endDate.ToDateTime(endTime), BuildingOffset).ToUniversalTime();

        return (start, end);
    }

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
