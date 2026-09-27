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

/// <summary>
/// RNF-005: proves two truly concurrent, mutually incompatible requests
/// against real PostgreSQL can never both succeed. Uses genuinely parallel
/// HTTP calls (started before either is awaited) rather than mocking DB
/// concurrency, per issue #23.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class ReservationConcurrencyTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly TimeSpan BuildingOffset = TimeSpan.FromHours(-3);
    private static readonly TimeOnly AfternoonStart = new(14, 0);
    private static readonly TimeOnly AfternoonEnd = new(19, 0);

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("environment", "Development"));

    private readonly List<string> _residentEmails = [];

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var units = await dbContext.Units
            .Where(unit => unit.BuildingId == DevelopmentDataSeeder.PilotBuildingId)
            .ToListAsync(cancellationToken);

        // Enough distinct memberships for every concurrency scenario below
        // to use its own pair of residents. Several residents share a unit
        // (allowed: the uniqueness constraint is per user/unit/building
        // triple, not one-resident-per-unit).
        const int residentCount = 12;

        for (var i = 0; i < residentCount; i++)
        {
            var unit = units[i % units.Count];
            var email = $"concurrency-resident-{i}-{Guid.NewGuid():N}@example.test";
            var user = new UserAccount(Guid.NewGuid(), email, $"Concurrency Resident {i}");
            Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
            Assert.True(
                (await userManager.AddToRoleAsync(user, ApplicationRoles.Resident))
                    .Succeeded);

            dbContext.ResidentMemberships.Add(new ResidentMembership(
                Guid.NewGuid(),
                unit.BuildingId,
                unit.Id,
                user.Id,
                DateTimeOffset.UtcNow));

            _residentEmails.Add(email);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task TwoConcurrentIncompatibleLeisureRequests_ExactlyOneWins()
    {
        var start = ToUtc(new DateOnly(2027, 6, 1), new TimeOnly(10, 0));
        var end = ToUtc(new DateOnly(2027, 6, 1), new TimeOnly(11, 0));

        using var clientA = await CreateAuthenticatedClientAsync(_residentEmails[0]);
        using var clientB = await CreateAuthenticatedClientAsync(_residentEmails[1]);

        var requestA = LeisureRequest(start, end, "ExclusiveLeisure");
        var requestB = LeisureRequest(start, end, "ExclusiveLeisure");

        // Both requests are dispatched before either is awaited, so their
        // execution genuinely overlaps.
        var taskA = clientA.PostAsJsonAsync(
            "/api/reservations", requestA, TestContext.Current.CancellationToken);
        var taskB = clientB.PostAsJsonAsync(
            "/api/reservations", requestB, TestContext.Current.CancellationToken);

        var responses = await Task.WhenAll(taskA, taskB);

        AssertExactlyOneWinner(responses);
        await AssertExactlyOneBookingAsync(
            DevelopmentDataSeeder.PilotSumId,
            start,
            end);
    }

    [Fact]
    public async Task TwoConcurrentEvents_IncompatibleOnSharedAddOn_ExactlyOneWins()
    {
        var start = ToUtc(new DateOnly(2027, 6, 2), AfternoonStart);
        var end = ToUtc(new DateOnly(2027, 6, 2), AfternoonEnd);

        using var clientA = await CreateAuthenticatedClientAsync(_residentEmails[2]);
        using var clientB = await CreateAuthenticatedClientAsync(_residentEmails[3]);

        // Event A books SUM + Pool; Event B books a *different* SUM-less
        // combination that only shares the Pool — the conflict must be
        // detected on that shared add-on, exactly like the sequential case
        // in issue #21, but now under genuine concurrency.
        var eventA = EventRequest(
            start,
            end,
            addOnAmenityIds: [DevelopmentDataSeeder.PilotPoolId]);
        var eventB = EventRequest(
            start,
            end,
            addOnAmenityIds: [DevelopmentDataSeeder.PilotPoolId]);

        var taskA = clientA.PostAsJsonAsync(
            "/api/reservations", eventA, TestContext.Current.CancellationToken);
        var taskB = clientB.PostAsJsonAsync(
            "/api/reservations", eventB, TestContext.Current.CancellationToken);

        var responses = await Task.WhenAll(taskA, taskB);

        AssertExactlyOneWinner(responses);

        // Whichever Event lost must not have persisted the SUM either —
        // full atomicity, not just "the add-on didn't get double-booked".
        await AssertExactlyOneBookingAsync(
            DevelopmentDataSeeder.PilotSumId,
            start,
            end);
        await AssertExactlyOneBookingAsync(
            DevelopmentDataSeeder.PilotPoolId,
            start,
            end);
    }

    [Fact]
    public async Task TwoConcurrentRequestsForDifferentResources_BothSucceed()
    {
        var start = ToUtc(new DateOnly(2027, 6, 3), new TimeOnly(10, 0));
        var end = ToUtc(new DateOnly(2027, 6, 3), new TimeOnly(11, 0));

        // A dedicated amenity + price rule (not the shared pilot Barbecue,
        // which only has an Event add-on rule, not a standalone Leisure
        // base rule) so this test only needs one resource to be genuinely
        // independent of the SUM.
        var otherAmenityId = await CreateExclusiveLeisureAmenityAsync();

        using var clientA = await CreateAuthenticatedClientAsync(_residentEmails[4]);
        using var clientB = await CreateAuthenticatedClientAsync(_residentEmails[5]);

        // Both exclusive, same time window, but different amenities: the
        // per-Amenity advisory lock must not serialize unrelated resources
        // into a false conflict.
        var requestA = LeisureRequest(start, end, "ExclusiveLeisure");
        var requestB = new
        {
            buildingId = DevelopmentDataSeeder.PilotBuildingId,
            amenityId = otherAmenityId,
            useType = "ExclusiveLeisure",
            startsAtUtc = start,
            endsAtUtc = end
        };

        var taskA = clientA.PostAsJsonAsync(
            "/api/reservations", requestA, TestContext.Current.CancellationToken);
        var taskB = clientB.PostAsJsonAsync(
            "/api/reservations", requestB, TestContext.Current.CancellationToken);

        var responses = await Task.WhenAll(taskA, taskB);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
    }

    [Fact]
    public async Task TwoConcurrentContiguousRequests_BothSucceed()
    {
        var date = new DateOnly(2027, 6, 4);
        var firstStart = ToUtc(date, new TimeOnly(10, 0));
        var firstEnd = ToUtc(date, new TimeOnly(11, 0));
        var secondStart = firstEnd;
        var secondEnd = ToUtc(date, new TimeOnly(12, 0));

        using var clientA = await CreateAuthenticatedClientAsync(_residentEmails[6]);
        using var clientB = await CreateAuthenticatedClientAsync(_residentEmails[7]);

        var requestA = LeisureRequest(firstStart, firstEnd, "ExclusiveLeisure");
        var requestB = LeisureRequest(secondStart, secondEnd, "ExclusiveLeisure");

        var taskA = clientA.PostAsJsonAsync(
            "/api/reservations", requestA, TestContext.Current.CancellationToken);
        var taskB = clientB.PostAsJsonAsync(
            "/api/reservations", requestB, TestContext.Current.CancellationToken);

        var responses = await Task.WhenAll(taskA, taskB);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
    }

    [Fact]
    public async Task ActiveHold_BlocksIncompatibleConcurrentRequest()
    {
        var start = ToUtc(new DateOnly(2027, 6, 5), new TimeOnly(10, 0));
        var end = ToUtc(new DateOnly(2027, 6, 5), new TimeOnly(11, 0));

        using var clientA = await CreateAuthenticatedClientAsync(_residentEmails[8]);
        var first = await clientA.PostAsJsonAsync(
            "/api/reservations",
            LeisureRequest(start, end, "ExclusiveLeisure"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<ReservationResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(firstBody);
        // Still just a hold — not a payment confirmation — yet it already
        // blocks a conflicting request (RB-009).
        Assert.Equal("Pending", firstBody.Status);

        using var clientB = await CreateAuthenticatedClientAsync(_residentEmails[9]);
        var second = await clientB.PostAsJsonAsync(
            "/api/reservations",
            LeisureRequest(start, end, "SharedLeisure"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task ExpiredHold_DoesNotBlockNewRequest()
    {
        var start = ToUtc(new DateOnly(2027, 6, 6), new TimeOnly(10, 0));
        var end = ToUtc(new DateOnly(2027, 6, 6), new TimeOnly(11, 0));

        using var clientA = await CreateAuthenticatedClientAsync(_residentEmails[10]);
        var first = await clientA.PostAsJsonAsync(
            "/api/reservations",
            LeisureRequest(start, end, "ExclusiveLeisure"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<ReservationResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(firstBody);

        // Simulate the hold's own deadline already having passed — the
        // conflict check must stop treating it as active immediately,
        // independent of whether the background expiration job has run.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Reservations\" SET \"ExpiresAtUtc\" = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE \"Id\" = {firstBody.Id}",
                TestContext.Current.CancellationToken);
        }

        using var clientB = await CreateAuthenticatedClientAsync(_residentEmails[11]);
        var second = await clientB.PostAsJsonAsync(
            "/api/reservations",
            LeisureRequest(start, end, "ExclusiveLeisure"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    private async Task<Guid> CreateExclusiveLeisureAmenityAsync()
    {
        var amenityId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var amenity = new Amenity(
            amenityId,
            DevelopmentDataSeeder.PilotBuildingId,
            $"Concurrency Test Amenity {amenityId:N}",
            AmenityKind.Other,
            allowsSharedUse: false,
            allowsExclusiveUse: true);

        for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
        {
            amenity.AddAvailabilityWindow(
                Guid.NewGuid(),
                day,
                new TimeOnly(0, 0),
                new TimeOnly(23, 59));
        }

        dbContext.Amenities.Add(amenity);

        dbContext.PriceRules.Add(new PriceRule(
            Guid.NewGuid(),
            DevelopmentDataSeeder.PilotBuildingId,
            amenityId,
            PriceComponentType.Base,
            ReservationUseType.ExclusiveLeisure,
            "ARS",
            1_000m,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null));

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return amenityId;
    }

    private static void AssertExactlyOneWinner(HttpResponseMessage[] responses)
    {
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);
    }

    private async Task AssertExactlyOneBookingAsync(
        Guid amenityId,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var count = await dbContext.ReservationResources
            .Where(resource =>
                resource.AmenityId == amenityId &&
                resource.Reservation.StartsAtUtc == start &&
                resource.Reservation.EndsAtUtc == end &&
                resource.Reservation.Status != ReservationStatus.Cancelled)
            .CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), BuildingOffset).ToUniversalTime();

    private static object LeisureRequest(
        DateTimeOffset start,
        DateTimeOffset end,
        string useType) =>
        new
        {
            buildingId = DevelopmentDataSeeder.PilotBuildingId,
            amenityId = DevelopmentDataSeeder.PilotSumId,
            useType,
            startsAtUtc = start,
            endsAtUtc = end
        };

    private static object EventRequest(
        DateTimeOffset start,
        DateTimeOffset end,
        Guid[] addOnAmenityIds) =>
        new
        {
            buildingId = DevelopmentDataSeeder.PilotBuildingId,
            amenityId = DevelopmentDataSeeder.PilotSumId,
            addOnAmenityIds,
            useType = "Event",
            startsAtUtc = start,
            endsAtUtc = end
        };

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

    private sealed record ReservationResponse(Guid Id, string Status);
}
