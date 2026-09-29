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
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Reservations;

/// <summary>
/// The resident-facing <c>GET /api/buildings/{buildingId}/event-slots</c>
/// endpoint (issue #62) — distinct from the Administrator-only
/// <c>/api/admin/buildings/{buildingId}/event-slots</c> covered elsewhere.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class ResidentEventSlotEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly TimeSpan BuenosAiresOffset = TimeSpan.FromHours(-3);

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

        _residentEmail = $"event-slot-resident-{Guid.NewGuid():N}@example.test";
        _adminEmail = $"event-slot-admin-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit1A = await dbContext.Units
            .SingleAsync(unit => unit.Label == "1A", cancellationToken);

        var resident = new UserAccount(Guid.NewGuid(), _residentEmail, "Event Slot Resident");
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

        // An Administrator satisfies ResidentAccess and
        // IBuildingMembershipAuthorizer.HasAccessAsync for any building
        // without needing a per-building ResidentMembership — used by the
        // tests below that exercise timezone/DST/empty-list behavior on a
        // fresh, single-purpose building rather than membership itself.
        var admin = new UserAccount(Guid.NewGuid(), _adminEmail, "Event Slot Administrator");
        Assert.True((await userManager.CreateAsync(admin, Password)).Succeeded);
        Assert.True(
            (await userManager.AddToRoleAsync(admin, ApplicationRoles.Administrator))
                .Succeeded);

        // A building the resident above has no membership in, for the 403
        // case — its time zone is irrelevant to that test.
        var otherBuilding = new Building(
            Guid.NewGuid(),
            "Other Event Slot Building",
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
    public async Task List_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/buildings/{DevelopmentDataSeeder.PilotBuildingId}/event-slots?date=2027-08-01",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ResidentWithoutMembershipInBuilding_Returns403()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/buildings/{_otherBuildingId}/event-slots?date=2027-08-01",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_AuthorizedResident_ReturnsActiveSlotsAsUtc()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/buildings/{DevelopmentDataSeeder.PilotBuildingId}/event-slots?date=2027-08-02",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var occurrences = await response.Content
            .ReadFromJsonAsync<List<EventSlotOccurrenceResponse>>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(occurrences);
        // Seeded pilot slots: "Placeholder afternoon slot" 14:00-19:00 and
        // "Placeholder evening slot" 19:00-22:00, building time zone
        // America/Argentina/Buenos_Aires (fixed -03:00, no DST there).
        Assert.Equal(2, occurrences.Count);

        var afternoon = occurrences.Single(o => o.Name == "Placeholder afternoon slot");
        Assert.Equal(ToUtc(new DateOnly(2027, 8, 2), new TimeOnly(14, 0)), afternoon.StartsAtUtc);
        Assert.Equal(ToUtc(new DateOnly(2027, 8, 2), new TimeOnly(19, 0)), afternoon.EndsAtUtc);

        var evening = occurrences.Single(o => o.Name == "Placeholder evening slot");
        Assert.Equal(ToUtc(new DateOnly(2027, 8, 2), new TimeOnly(19, 0)), evening.StartsAtUtc);
        Assert.Equal(ToUtc(new DateOnly(2027, 8, 2), new TimeOnly(22, 0)), evening.EndsAtUtc);
    }

    [Fact]
    public async Task List_OrdersByStartThenEndTime()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/buildings/{DevelopmentDataSeeder.PilotBuildingId}/event-slots?date=2027-08-03",
            TestContext.Current.CancellationToken);

        var occurrences = await response.Content
            .ReadFromJsonAsync<List<EventSlotOccurrenceResponse>>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(occurrences);
        Assert.True(occurrences.Count >= 2);
        Assert.True(
            occurrences.Zip(occurrences.Skip(1))
                .All(pair => pair.First.StartsAtUtc <= pair.Second.StartsAtUtc));
    }

    [Fact]
    public async Task List_ReturnsEmptyArray_WhenBuildingHasNoActiveSlots()
    {
        Guid buildingId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var building = new Building(
                Guid.NewGuid(),
                "No Event Slots Building",
                "America/Argentina/Buenos_Aires");
            dbContext.Buildings.Add(building);
            buildingId = building.Id;

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedAdminClientAsync();

        var response = await client.GetAsync(
            $"/api/buildings/{buildingId}/event-slots?date=2027-08-04",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var occurrences = await response.Content
            .ReadFromJsonAsync<List<EventSlotOccurrenceResponse>>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(occurrences);
        Assert.Empty(occurrences);
    }

    [Fact]
    public async Task List_ExcludesDeactivatedSlot_AndReincludesOnReactivation()
    {
        Guid buildingId;
        Guid slotId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // A dedicated, throwaway building: this test mutates
            // IsActive, and the shared pilot building must never carry a
            // side effect across other tests in this collection (the same
            // rule AdminTestBase documents for administrative mutations).
            var building = new Building(
                Guid.NewGuid(),
                "Toggle Event Slot Building",
                "America/Argentina/Buenos_Aires");
            dbContext.Buildings.Add(building);
            buildingId = building.Id;

            var slot = new EventSlotDefinition(
                Guid.NewGuid(),
                building.Id,
                "Toggle slot",
                new TimeOnly(10, 0),
                new TimeOnly(11, 0));
            dbContext.EventSlotDefinitions.Add(slot);
            slotId = slot.Id;

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedAdminClientAsync();

        async Task<bool> SlotIsListedAsync()
        {
            var response = await client.GetAsync(
                $"/api/buildings/{buildingId}/event-slots?date=2027-08-05",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var occurrences = await response.Content
                .ReadFromJsonAsync<List<EventSlotOccurrenceResponse>>(
                    TestContext.Current.CancellationToken);

            Assert.NotNull(occurrences);
            return occurrences.Any(o => o.Id == slotId);
        }

        Assert.True(await SlotIsListedAsync());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var slot = await dbContext.EventSlotDefinitions.SingleAsync(
                candidate => candidate.Id == slotId,
                TestContext.Current.CancellationToken);
            slot.Deactivate();
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.False(await SlotIsListedAsync());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var slot = await dbContext.EventSlotDefinitions.SingleAsync(
                candidate => candidate.Id == slotId,
                TestContext.Current.CancellationToken);
            slot.Activate();
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(await SlotIsListedAsync());
    }

    [Fact]
    public async Task List_DoesNotHideASlot_WhenAnIncompatibleReservationAlreadyOccupiesIt()
    {
        var date = new DateOnly(2027, 8, 6);
        var (startUtc, endUtc) = (
            ToUtc(date, new TimeOnly(14, 0)),
            ToUtc(date, new TimeOnly(19, 0)));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var membershipId = await dbContext.ResidentMemberships
                .Where(membership => membership.User.Email == _residentEmail)
                .Select(membership => membership.Id)
                .SingleAsync(TestContext.Current.CancellationToken);

            var existingReservation = new Reservation(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotBuildingId,
                membershipId,
                ReservationUseType.Event,
                startUtc,
                endUtc,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(1));
            existingReservation.AddResource(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotSumId,
                isExclusive: true);

            dbContext.Reservations.Add(existingReservation);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.GetAsync(
            $"/api/buildings/{DevelopmentDataSeeder.PilotBuildingId}/event-slots?date={date:yyyy-MM-dd}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var occurrences = await response.Content
            .ReadFromJsonAsync<List<EventSlotOccurrenceResponse>>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(occurrences);
        // This is the whole point of the issue: a fully-booked slot is
        // still a *configured* slot. Whether it can actually be booked is
        // decided only by POST /api/reservations (409 on conflict).
        Assert.Contains(occurrences, o => o.Name == "Placeholder afternoon slot");
    }

    [Fact]
    public async Task List_DifferentTimeZone_ConvertsCorrectly_NotHardcodedBuenosAires()
    {
        Guid buildingId;
        Guid slotId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // A January date is standard time (EST, UTC-5) in
            // America/New_York — no DST in effect, so this isolates plain
            // timezone-offset correctness from the DST-specific tests below.
            var building = new Building(Guid.NewGuid(), "New York Building", "America/New_York");
            dbContext.Buildings.Add(building);
            buildingId = building.Id;

            var slot = new EventSlotDefinition(
                Guid.NewGuid(),
                building.Id,
                "Morning",
                new TimeOnly(9, 0),
                new TimeOnly(11, 0));
            dbContext.EventSlotDefinitions.Add(slot);
            slotId = slot.Id;

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedAdminClientAsync();

        var response = await client.GetAsync(
            $"/api/buildings/{buildingId}/event-slots?date=2027-01-15",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var occurrences = await response.Content
            .ReadFromJsonAsync<List<EventSlotOccurrenceResponse>>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(occurrences);
        var occurrence = Assert.Single(occurrences);
        Assert.Equal(slotId, occurrence.Id);
        // 09:00/11:00 EST (UTC-5) on 2027-01-15.
        Assert.Equal(
            new DateTimeOffset(2027, 1, 15, 14, 0, 0, TimeSpan.Zero),
            occurrence.StartsAtUtc);
        Assert.Equal(
            new DateTimeOffset(2027, 1, 15, 16, 0, 0, TimeSpan.Zero),
            occurrence.EndsAtUtc);
    }

    [Fact]
    public async Task List_DstInvalidLocalTime_ReturnsUnprocessable()
    {
        Guid buildingId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var building = new Building(Guid.NewGuid(), "DST Spring Building", "America/New_York");
            dbContext.Buildings.Add(building);
            buildingId = building.Id;

            // 2028-03-12 is the America/New_York spring-forward DST
            // transition (clocks jump 02:00 -> 03:00), so 02:15-02:45 does
            // not exist that day.
            dbContext.EventSlotDefinitions.Add(new EventSlotDefinition(
                Guid.NewGuid(),
                building.Id,
                "Invalid window",
                new TimeOnly(2, 15),
                new TimeOnly(2, 45)));

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedAdminClientAsync();

        var response = await client.GetAsync(
            $"/api/buildings/{buildingId}/event-slots?date=2028-03-12",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task List_DstAmbiguousLocalTime_ReturnsUnprocessable()
    {
        Guid buildingId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var building = new Building(Guid.NewGuid(), "DST Fall Building", "America/New_York");
            dbContext.Buildings.Add(building);
            buildingId = building.Id;

            // 2028-11-05 is the America/New_York fall-back DST transition
            // (clocks repeat 01:00-02:00), so 01:15-01:45 occurs twice.
            dbContext.EventSlotDefinitions.Add(new EventSlotDefinition(
                Guid.NewGuid(),
                building.Id,
                "Ambiguous window",
                new TimeOnly(1, 15),
                new TimeOnly(1, 45)));

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedAdminClientAsync();

        var response = await client.GetAsync(
            $"/api/buildings/{buildingId}/event-slots?date=2028-11-05",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), BuenosAiresOffset).ToUniversalTime();

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

    /// <summary>
    /// An Administrator satisfies <c>ResidentAccess</c> and
    /// <see cref="Modules.Buildings.Application.IBuildingMembershipAuthorizer.HasAccessAsync"/>
    /// for any building, which keeps tests unrelated to membership itself
    /// (timezone/DST/empty-list) from needing a dedicated per-building
    /// resident membership.
    /// </summary>
    private Task<HttpClient> CreateAuthenticatedAdminClientAsync() =>
        CreateAuthenticatedClientAsync(_adminEmail);

    private sealed record EventSlotOccurrenceResponse(
        Guid Id,
        string Name,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc);
}
