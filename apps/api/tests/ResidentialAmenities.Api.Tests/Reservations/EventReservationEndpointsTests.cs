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
public sealed class EventReservationEndpointsTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly TimeSpan BuildingOffset = TimeSpan.FromHours(-3);

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("environment", "Development"));

    private string _residentEmail = string.Empty;
    private string _secondResidentEmail = string.Empty;
    private Guid _otherBuildingId;
    private Guid _otherBuildingSumId;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _residentEmail = $"event-resident-{Guid.NewGuid():N}@example.test";
        _secondResidentEmail = $"event-resident2-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit1A = await dbContext.Units
            .SingleAsync(unit => unit.Label == "1A", cancellationToken);
        var unit1B = await dbContext.Units
            .SingleAsync(unit => unit.Label == "1B", cancellationToken);

        var resident = new UserAccount(Guid.NewGuid(), _residentEmail, "Event Resident One");
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
            new UserAccount(Guid.NewGuid(), _secondResidentEmail, "Event Resident Two");
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
            "Other Event Building",
            "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(otherBuilding);
        _otherBuildingId = otherBuilding.Id;

        var otherSum = new Amenity(
            Guid.NewGuid(),
            otherBuilding.Id,
            "Other SUM",
            AmenityKind.Sum,
            allowsSharedUse: true,
            allowsExclusiveUse: true);
        dbContext.Amenities.Add(otherSum);
        _otherBuildingSumId = otherSum.Id;

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
            BuildEventRequest(new DateOnly(2027, 4, 1), AfternoonStart, AfternoonEnd),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ResidentWithoutMembership_Returns403()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                new DateOnly(2027, 4, 1),
                AfternoonStart,
                AfternoonEnd,
                buildingId: _otherBuildingId,
                baseAmenityId: _otherBuildingSumId),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_SumOnly_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(new DateOnly(2027, 4, 2), AfternoonStart, AfternoonEnd),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal("Event", body.UseType);
        Assert.Single(body.Resources);
        Assert.True(body.Resources[0].IsExclusive);
        Assert.Equal(15_000m, body.TotalAmount);
    }

    [Fact]
    public async Task Create_SumPlusPool_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                new DateOnly(2027, 4, 3),
                AfternoonStart,
                AfternoonEnd,
                addOnAmenityIds: [DevelopmentDataSeeder.PilotPoolId]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(2, body.Resources.Count);
        Assert.Equal(18_000m, body.TotalAmount);
    }

    [Fact]
    public async Task Create_SumPlusBarbecue_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                new DateOnly(2027, 4, 4),
                AfternoonStart,
                AfternoonEnd,
                addOnAmenityIds: [DevelopmentDataSeeder.PilotBarbecueId]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(2, body.Resources.Count);
        Assert.Equal(18_000m, body.TotalAmount);
    }

    [Fact]
    public async Task Create_SumPlusPoolPlusBarbecue_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                new DateOnly(2027, 4, 5),
                AfternoonStart,
                AfternoonEnd,
                addOnAmenityIds:
                [
                    DevelopmentDataSeeder.PilotPoolId,
                    DevelopmentDataSeeder.PilotBarbecueId
                ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(3, body.Resources.Count);
        Assert.True(body.Resources.All(resource => resource.IsExclusive));
        Assert.Equal(3, body.PriceLines.Count);
        Assert.Equal(21_000m, body.TotalAmount);
        Assert.Equal("ARS", body.Currency);
    }

    [Fact]
    public async Task Create_BaseNotSum_ReturnsUnprocessable()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                new DateOnly(2027, 4, 6),
                AfternoonStart,
                AfternoonEnd,
                baseAmenityId: DevelopmentDataSeeder.PilotPoolId),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateAddOn_ReturnsBadRequest()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                new DateOnly(2027, 4, 7),
                AfternoonStart,
                AfternoonEnd,
                addOnAmenityIds:
                [
                    DevelopmentDataSeeder.PilotPoolId,
                    DevelopmentDataSeeder.PilotPoolId
                ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_OtherKindAddOn_ReturnsUnprocessable()
    {
        var otherAmenityId = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var other = new Amenity(
                otherAmenityId,
                DevelopmentDataSeeder.PilotBuildingId,
                $"Coworking {otherAmenityId:N}",
                AmenityKind.Other,
                allowsSharedUse: false,
                allowsExclusiveUse: true);

            for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
            {
                other.AddAvailabilityWindow(
                    Guid.NewGuid(),
                    day,
                    new TimeOnly(9, 0),
                    new TimeOnly(22, 0));
            }

            dbContext.Amenities.Add(other);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                new DateOnly(2027, 4, 8),
                AfternoonStart,
                AfternoonEnd,
                addOnAmenityIds: [otherAmenityId]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_AddOnFromOtherBuilding_ReturnsNotFound()
    {
        var otherBuildingPoolId = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var pool = new Amenity(
                otherBuildingPoolId,
                _otherBuildingId,
                $"Other Pool {otherBuildingPoolId:N}",
                AmenityKind.Pool,
                allowsSharedUse: true,
                allowsExclusiveUse: true);

            dbContext.Amenities.Add(pool);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                new DateOnly(2027, 4, 9),
                AfternoonStart,
                AfternoonEnd,
                addOnAmenityIds: [otherBuildingPoolId]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_AddOnDuringMaintenance_ReturnsUnprocessable()
    {
        var date = new DateOnly(2027, 4, 10);
        var (startUtc, endUtc) = ToUtcRange(date, AfternoonStart, AfternoonEnd);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            dbContext.AmenityUnavailablePeriods.Add(new AmenityUnavailablePeriod(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotBarbecueId,
                startUtc,
                endUtc,
                "Grill maintenance"));

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                date,
                AfternoonStart,
                AfternoonEnd,
                addOnAmenityIds: [DevelopmentDataSeeder.PilotBarbecueId]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_AddOnOutsideItsAvailability_ReturnsUnprocessable()
    {
        var limitedPoolId = Guid.NewGuid();
        var date = new DateOnly(2027, 4, 11); // a Sunday

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var limitedPool = new Amenity(
                limitedPoolId,
                DevelopmentDataSeeder.PilotBuildingId,
                $"Limited Pool {limitedPoolId:N}",
                AmenityKind.Pool,
                allowsSharedUse: true,
                allowsExclusiveUse: true);

            // Only open on Mondays; the test date above is a Sunday, so any
            // Event range on that date is outside this resource's
            // availability regardless of the general SUM/Event slot.
            limitedPool.AddAvailabilityWindow(
                Guid.NewGuid(),
                DayOfWeek.Monday,
                new TimeOnly(9, 0),
                new TimeOnly(22, 0));

            dbContext.Amenities.Add(limitedPool);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                date,
                AfternoonStart,
                AfternoonEnd,
                addOnAmenityIds: [limitedPoolId]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_OutsideConfiguredEventSlot_ReturnsUnprocessable()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        // 13:17-16:43 falls fully within the SUM's general 09:00-22:00
        // availability but does not match any configured EventSlotDefinition
        // — exactly the case this issue must reject.
        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                new DateOnly(2027, 4, 12),
                new TimeOnly(13, 17),
                new TimeOnly(16, 43)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_ConflictOnSum_ReturnsConflict()
    {
        var date = new DateOnly(2027, 4, 13);

        using var firstClient = await CreateAuthenticatedClientAsync(_residentEmail);
        var first = await firstClient.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(date, AfternoonStart, AfternoonEnd),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var secondClient =
            await CreateAuthenticatedClientAsync(_secondResidentEmail);
        var second = await secondClient.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(date, AfternoonStart, AfternoonEnd),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_ConflictOnlyOnAddOn_RejectsWholeEvent()
    {
        var date = new DateOnly(2027, 4, 14);
        var (startUtc, endUtc) = ToUtcRange(date, AfternoonStart, AfternoonEnd);

        // An unrelated confirmed exclusive booking directly on the Pool,
        // overlapping the afternoon slot, inserted straight through EF: a
        // standalone Pool reservation isn't creatable via the API yet
        // (OQ-014 is still open), but this issue only needs a pre-existing
        // incompatible booking on that resource to exist. The SUM itself
        // must stay free.
        await using (var setupScope = _factory.Services.CreateAsyncScope())
        {
            var setupDbContext =
                setupScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var existingMembershipId = await setupDbContext.ResidentMemberships
                .Where(membership => membership.User.Email == _residentEmail)
                .Select(membership => membership.Id)
                .SingleAsync(TestContext.Current.CancellationToken);

            var existingPoolReservation = new Reservation(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotBuildingId,
                existingMembershipId,
                ReservationUseType.ExclusiveLeisure,
                startUtc,
                endUtc,
                DateTimeOffset.UtcNow);
            existingPoolReservation.AddResource(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotPoolId,
                isExclusive: true);

            setupDbContext.Reservations.Add(existingPoolReservation);
            await setupDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var secondClient =
            await CreateAuthenticatedClientAsync(_secondResidentEmail);
        var eventResponse = await secondClient.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                date,
                AfternoonStart,
                AfternoonEnd,
                addOnAmenityIds: [DevelopmentDataSeeder.PilotPoolId]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, eventResponse.StatusCode);

        // The SUM itself must remain unbooked: the Event was rejected
        // atomically, not partially persisted.
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sumBookingsThatDay = await dbContext.ReservationResources
            .Where(resource => resource.AmenityId == DevelopmentDataSeeder.PilotSumId)
            .Where(resource =>
                resource.Reservation.StartsAtUtc == startUtc &&
                resource.Reservation.EndsAtUtc == endUtc)
            .CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, sumBookingsThatDay);
    }

    [Fact]
    public async Task Create_ContiguousEventSlotsSameDay_BothSucceed()
    {
        var date = new DateOnly(2027, 4, 15);

        using var firstClient = await CreateAuthenticatedClientAsync(_residentEmail);
        var afternoon = await firstClient.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(date, AfternoonStart, AfternoonEnd),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, afternoon.StatusCode);

        using var secondClient =
            await CreateAuthenticatedClientAsync(_secondResidentEmail);
        var evening = await secondClient.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(date, EveningStart, EveningEnd),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, evening.StatusCode);
    }

    [Fact]
    public async Task Create_IgnoresClientSuppliedPrice()
    {
        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var (startUtc, endUtc) = ToUtcRange(
            new DateOnly(2027, 4, 16),
            AfternoonStart,
            AfternoonEnd);

        var payload = new
        {
            buildingId = DevelopmentDataSeeder.PilotBuildingId,
            amenityId = DevelopmentDataSeeder.PilotSumId,
            addOnAmenityIds = new[] { DevelopmentDataSeeder.PilotPoolId },
            useType = "Event",
            startsAtUtc = startUtc,
            endsAtUtc = endUtc,
            totalAmount = 1m,
            priceLines = Array.Empty<object>()
        };

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            payload,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(18_000m, body.TotalAmount);
    }

    [Fact]
    public async Task ChangingAddOnPriceRuleLater_DoesNotAlterExistingEventSnapshot()
    {
        // Dedicated amenity/rule (not the shared pilot Pool rule) so
        // superseding it cannot affect any other test in this collection.
        var addOnId = Guid.NewGuid();
        var effectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var addOn = new Amenity(
                addOnId,
                DevelopmentDataSeeder.PilotBuildingId,
                $"Snapshot AddOn {addOnId:N}",
                AmenityKind.Pool,
                allowsSharedUse: true,
                allowsExclusiveUse: true);

            for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
            {
                addOn.AddAvailabilityWindow(
                    Guid.NewGuid(),
                    day,
                    new TimeOnly(9, 0),
                    new TimeOnly(22, 0));
            }

            dbContext.Amenities.Add(addOn);

            dbContext.PriceRules.Add(new PriceRule(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotBuildingId,
                addOnId,
                PriceComponentType.AddOn,
                ReservationUseType.Event,
                "ARS",
                2_500m,
                effectiveFrom,
                null));

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = await CreateAuthenticatedClientAsync(_residentEmail);

        var created = await client.PostAsJsonAsync(
            "/api/reservations",
            BuildEventRequest(
                new DateOnly(2027, 4, 17),
                AfternoonStart,
                AfternoonEnd,
                addOnAmenityIds: [addOnId]),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var reservation = await created.Content
            .ReadFromJsonAsync<ReservationResponse>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(reservation);
        Assert.Equal(17_500m, reservation.TotalAmount); // 15,000 SUM + 2,500

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var currentRule = await dbContext.PriceRules.SingleAsync(
                rule =>
                    rule.AmenityId == addOnId &&
                    rule.ComponentType == PriceComponentType.AddOn &&
                    rule.UseType == ReservationUseType.Event,
                TestContext.Current.CancellationToken);

            var now = DateTimeOffset.UtcNow;
            currentRule.Supersede(now);

            dbContext.PriceRules.Add(new PriceRule(
                Guid.NewGuid(),
                DevelopmentDataSeeder.PilotBuildingId,
                addOnId,
                PriceComponentType.AddOn,
                ReservationUseType.Event,
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
        Assert.Equal(17_500m, refetchedBody.TotalAmount);
    }

    private static readonly TimeOnly AfternoonStart = new(14, 0);
    private static readonly TimeOnly AfternoonEnd = new(19, 0);
    private static readonly TimeOnly EveningStart = new(19, 0);
    private static readonly TimeOnly EveningEnd = new(22, 0);

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) ToUtcRange(
        DateOnly date,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        // Normalized to a zero UTC offset: Npgsql only persists
        // DateTimeOffset with Offset=0 into `timestamptz`, which matters
        // whenever a test writes directly through the DbContext instead of
        // going through the endpoint (which normalizes client input itself).
        var start = new DateTimeOffset(
            date.ToDateTime(startTime),
            BuildingOffset).ToUniversalTime();
        var end = new DateTimeOffset(
            date.ToDateTime(endTime),
            BuildingOffset).ToUniversalTime();

        return (start, end);
    }

    private static object BuildEventRequest(
        DateOnly date,
        TimeOnly startTime,
        TimeOnly endTime,
        Guid? buildingId = null,
        Guid? baseAmenityId = null,
        Guid[]? addOnAmenityIds = null)
    {
        var (startUtc, endUtc) = ToUtcRange(date, startTime, endTime);

        return new
        {
            buildingId = buildingId ?? DevelopmentDataSeeder.PilotBuildingId,
            amenityId = baseAmenityId ?? DevelopmentDataSeeder.PilotSumId,
            addOnAmenityIds = addOnAmenityIds ?? [],
            useType = "Event",
            startsAtUtc = startUtc,
            endsAtUtc = endUtc
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
