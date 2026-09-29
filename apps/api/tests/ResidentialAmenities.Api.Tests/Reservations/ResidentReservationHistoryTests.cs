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
/// Issue #66: the resident "my reservations" list, and the object-level
/// authorization fix on <c>GET /api/reservations/{id}</c> (a same-building
/// resident must not be able to read another resident's reservation just by
/// knowing/guessing its id).
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class ResidentReservationHistoryTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

    private string _residentAEmail = string.Empty;
    private string _residentBEmail = string.Empty;
    private string _adminEmail = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _residentAEmail = $"history-a-{Guid.NewGuid():N}@example.test";
        _residentBEmail = $"history-b-{Guid.NewGuid():N}@example.test";
        _adminEmail = $"history-admin-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // unit1A and unit1B both belong to the seeded pilot building — the
        // two residents below are neighbors in the same building.
        var unit1A = await dbContext.Units.SingleAsync(unit => unit.Label == "1A", cancellationToken);
        var unit1B = await dbContext.Units.SingleAsync(unit => unit.Label == "1B", cancellationToken);

        await AddResidentAsync(userManager, dbContext, _residentAEmail, unit1A);
        await AddResidentAsync(userManager, dbContext, _residentBEmail, unit1B);

        var admin = new UserAccount(Guid.NewGuid(), _adminEmail, "History Admin");
        Assert.True((await userManager.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(admin, ApplicationRoles.Administrator)).Succeeded);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    // --- listing -------------------------------------------------------------------

    [Fact]
    public async Task List_OwnerWithNoReservations_ReturnsEmptyPage()
    {
        using var client = await LoginAsync(_residentAEmail);

        var page = await ListAsync(client, DevelopmentDataSeeder.PilotBuildingId);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task List_MissingBuildingMembership_Returns403()
    {
        var otherBuildingId = await CreateOtherBuildingAsync();

        using var client = await LoginAsync(_residentAEmail);

        var response = await client.GetAsync(
            $"/api/reservations?buildingId={otherBuildingId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_OnlyReturnsReservationsCreatedByTheCallersOwnMembership()
    {
        using var clientA = await LoginAsync(_residentAEmail);
        using var clientB = await LoginAsync(_residentBEmail);

        var reservationA = await CreateReservationAsync(clientA, new DateOnly(2027, 5, 1));
        var reservationB = await CreateReservationAsync(clientB, new DateOnly(2027, 5, 2));

        var pageForA = await ListAsync(clientA, DevelopmentDataSeeder.PilotBuildingId);

        Assert.Single(pageForA.Items);
        Assert.Equal(reservationA, pageForA.Items[0].Id);
        Assert.DoesNotContain(pageForA.Items, item => item.Id == reservationB);
    }

    [Fact]
    public async Task List_OrdersByCreatedAtUtcDescending_WithIdAsTiebreaker()
    {
        using var client = await LoginAsync(_residentAEmail);

        var first = await CreateReservationAsync(client, new DateOnly(2027, 5, 10));
        var second = await CreateReservationAsync(client, new DateOnly(2027, 5, 11));
        var third = await CreateReservationAsync(client, new DateOnly(2027, 5, 12));

        var page = await ListAsync(client, DevelopmentDataSeeder.PilotBuildingId);

        Assert.Equal([third, second, first], page.Items.Select(item => item.Id).ToList());
    }

    [Fact]
    public async Task List_Pagination_ReturnsRequestedPageAndTotalCount()
    {
        using var client = await LoginAsync(_residentAEmail);

        for (var day = 15; day <= 19; day++)
        {
            await CreateReservationAsync(client, new DateOnly(2027, 5, day));
        }

        var firstPage = await ListAsync(client, DevelopmentDataSeeder.PilotBuildingId, page: 1, pageSize: 2);
        var secondPage = await ListAsync(client, DevelopmentDataSeeder.PilotBuildingId, page: 2, pageSize: 2);

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Empty(firstPage.Items.Select(i => i.Id).Intersect(secondPage.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task List_PageSizeAboveMaximum_IsClampedTo100()
    {
        using var client = await LoginAsync(_residentAEmail);
        await CreateReservationAsync(client, new DateOnly(2027, 5, 20));

        var page = await ListAsync(client, DevelopmentDataSeeder.PilotBuildingId, pageSize: 500);

        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task List_NeverAcceptsAMembershipOrUserIdFromTheClient()
    {
        using var clientA = await LoginAsync(_residentAEmail);
        using var clientB = await LoginAsync(_residentBEmail);

        var reservationB = await CreateReservationAsync(clientB, new DateOnly(2027, 5, 25));

        // Resident A tries to pass Resident B's membership/user id explicitly;
        // the server must still resolve ownership from A's own session.
        var response = await clientA.GetAsync(
            $"/api/reservations?buildingId={DevelopmentDataSeeder.PilotBuildingId}" +
            $"&membershipId={Guid.NewGuid()}&userId={Guid.NewGuid()}&createdByMembershipId={Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<ResidentReservationPageResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(page);
        Assert.DoesNotContain(page.Items, item => item.Id == reservationB);
    }

    // --- detail object-level authorization ------------------------------------------

    [Fact]
    public async Task Detail_Owner_Returns200()
    {
        using var client = await LoginAsync(_residentAEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 9, 1));

        var response = await client.GetAsync(
            $"/api/reservations/{reservationId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Detail_SameBuildingNonOwner_Returns403()
    {
        using var owner = await LoginAsync(_residentAEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 9, 2));

        using var neighbor = await LoginAsync(_residentBEmail);
        var response = await neighbor.GetAsync(
            $"/api/reservations/{reservationId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Detail_Administrator_CanReadAnyResidentsReservation()
    {
        using var owner = await LoginAsync(_residentAEmail);
        var reservationId = await CreateReservationAsync(owner, new DateOnly(2027, 9, 3));

        using var admin = await LoginAsync(_adminEmail);
        var response = await admin.GetAsync(
            $"/api/reservations/{reservationId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Detail_MissingReservation_Returns404()
    {
        using var client = await LoginAsync(_residentAEmail);

        var response = await client.GetAsync(
            $"/api/reservations/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_SerializesLifecycleFields_ForACancelledReservation()
    {
        using var client = await LoginAsync(_residentAEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 9, 4));

        var cancelledAtUtc = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reservation = await dbContext.Reservations.SingleAsync(
                r => r.Id == reservationId, TestContext.Current.CancellationToken);

            reservation.Cancel(cancelledAtUtc, "Resident requested cancellation.");
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var response = await client.GetAsync(
            $"/api/reservations/{reservationId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReservationDetailResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal("Cancelled", body.Status);
        Assert.NotNull(body.CancelledAtUtc);
        Assert.Equal("Resident requested cancellation.", body.CancellationReason);
        Assert.Null(body.ConfirmedAtUtc);
        Assert.Null(body.ExpiredAtUtc);
    }

    [Fact]
    public async Task Detail_And_List_UseThePriceSnapshot_NeverTheCurrentPriceRule()
    {
        var amenityId = Guid.NewGuid();
        var effectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var amenity = new Amenity(
                amenityId,
                DevelopmentDataSeeder.PilotBuildingId,
                $"History Snapshot Amenity {amenityId:N}",
                AmenityKind.Other,
                allowsSharedUse: true,
                allowsExclusiveUse: true);

            for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
            {
                amenity.AddAvailabilityWindow(Guid.NewGuid(), day, new TimeOnly(9, 0), new TimeOnly(22, 0));
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

        using var client = await LoginAsync(_residentAEmail);
        var reservationId = await CreateReservationAsync(client, new DateOnly(2027, 9, 5), amenityId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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

        var detail = await client.GetFromJsonAsync<ReservationDetailResponse>(
            $"/api/reservations/{reservationId}", JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(detail);
        Assert.Equal(4_000m, detail.TotalAmount);

        var page = await ListAsync(client, DevelopmentDataSeeder.PilotBuildingId);
        var listed = page.Items.Single(item => item.Id == reservationId);
        Assert.Equal(4_000m, listed.TotalAmount);
    }

    // --- helpers -----------------------------------------------------------------

    private async Task<Guid> CreateOtherBuildingAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var building = new Building(
            Guid.NewGuid(), "Other History Building", "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(building);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return building.Id;
    }

    private async Task<Guid> CreateReservationAsync(
        HttpClient client,
        DateOnly date,
        Guid? amenityId = null)
    {
        var start = new DateTimeOffset(
            date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(-3)).ToUniversalTime();

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            new
            {
                buildingId = DevelopmentDataSeeder.PilotBuildingId,
                amenityId = amenityId ?? DevelopmentDataSeeder.PilotSumId,
                useType = "SharedLeisure",
                startsAtUtc = start,
                endsAtUtc = start.AddHours(1)
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedReservation>(
            JsonOptions, TestContext.Current.CancellationToken);
        return body!.Id;
    }

    private static async Task<ResidentReservationPageResponse> ListAsync(
        HttpClient client,
        Guid buildingId,
        int? page = null,
        int? pageSize = null)
    {
        var query = $"buildingId={buildingId}";

        if (page is { } p)
        {
            query += $"&page={p}";
        }

        if (pageSize is { } ps)
        {
            query += $"&pageSize={ps}";
        }

        var response = await client.GetAsync(
            $"/api/reservations?{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ResidentReservationPageResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        return body!;
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

    private static async Task AddResidentAsync(
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        string email,
        Unit unit)
    {
        var user = new UserAccount(Guid.NewGuid(), email, email);
        Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, ApplicationRoles.Resident)).Succeeded);

        dbContext.ResidentMemberships.Add(new ResidentMembership(
            Guid.NewGuid(), unit.BuildingId, unit.Id, user.Id, DateTimeOffset.UtcNow));
    }

    private sealed record CreatedReservation(Guid Id);

    private sealed record ReservationDetailResponse(
        Guid Id,
        Guid BuildingId,
        string UseType,
        string Status,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? ConfirmedAtUtc,
        DateTimeOffset? CancelledAtUtc,
        DateTimeOffset? ExpiredAtUtc,
        string? CancellationReason,
        decimal TotalAmount);

    private sealed record ResidentReservationSummaryResponse(
        Guid Id,
        Guid BuildingId,
        string UseType,
        string Status,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? ConfirmedAtUtc,
        DateTimeOffset? CancelledAtUtc,
        DateTimeOffset? ExpiredAtUtc,
        string? CancellationReason,
        decimal TotalAmount);

    private sealed record ResidentReservationPageResponse(
        List<ResidentReservationSummaryResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);
}
