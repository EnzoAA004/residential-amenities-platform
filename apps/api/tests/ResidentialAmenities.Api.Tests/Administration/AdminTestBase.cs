using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Administration;

/// <summary>
/// Every administrative test gets its own building (a SUM, a court, weekly
/// windows, price rules, placeholder event slots, a resident and an
/// administrator). Administrative operations change configuration, so they
/// must never run against the shared pilot building other tests depend on.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public abstract class AdminTestBase : IAsyncLifetime
{
    protected const string Password = "Test!Password123";
    protected static readonly TimeSpan BuildingOffset = TimeSpan.FromHours(-3);
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected readonly WebApplicationFactory<Program> Factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

    protected Guid BuildingId { get; private set; }

    protected Guid SumId { get; private set; }

    protected Guid CourtId { get; private set; }

    protected Guid MembershipId { get; private set; }

    protected Guid ResidentId { get; private set; }

    protected Guid AdminId { get; private set; }

    protected string ResidentEmail { get; private set; } = string.Empty;

    protected string AdminEmail { get; private set; } = string.Empty;

    public virtual async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        ResidentEmail = $"adm-resident-{Guid.NewGuid():N}@example.test";
        AdminEmail = $"adm-admin-{Guid.NewGuid():N}@example.test";

        await using var scope = Factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var building = new Building(
            Guid.NewGuid(), $"Admin Test Building {Guid.NewGuid():N}", "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(building);
        BuildingId = building.Id;

        // A unique label: other tests look up the pilot units "1A"/"1B" with SingleAsync.
        var unit = new Unit(Guid.NewGuid(), building.Id, 1, "ADM", $"A{Guid.NewGuid():N}"[..12]);
        dbContext.Units.Add(unit);

        var sum = NewAmenity(building.Id, "SUM", AmenityKind.Sum);
        var court = NewAmenity(building.Id, "Court", AmenityKind.Other);
        dbContext.Amenities.AddRange(sum, court);
        SumId = sum.Id;
        CourtId = court.Id;

        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        dbContext.PriceRules.AddRange(
            Rule(building.Id, sum.Id, PriceComponentType.Base, ReservationUseType.SharedLeisure, 5_000m, from),
            Rule(building.Id, sum.Id, PriceComponentType.Base, ReservationUseType.ExclusiveLeisure, 8_000m, from),
            Rule(building.Id, sum.Id, PriceComponentType.Base, ReservationUseType.Event, 15_000m, from),
            Rule(building.Id, court.Id, PriceComponentType.Base, ReservationUseType.SharedLeisure, 2_000m, from),
            Rule(building.Id, court.Id, PriceComponentType.Base, ReservationUseType.ExclusiveLeisure, 4_000m, from));

        // Placeholder slots only (real times are pending issue #2).
        dbContext.EventSlotDefinitions.AddRange(
            new EventSlotDefinition(Guid.NewGuid(), building.Id, "Test afternoon", new TimeOnly(14, 0), new TimeOnly(19, 0)),
            new EventSlotDefinition(Guid.NewGuid(), building.Id, "Test evening", new TimeOnly(19, 0), new TimeOnly(22, 0)));

        var resident = new UserAccount(Guid.NewGuid(), ResidentEmail, "Admin Test Resident");
        Assert.True((await userManager.CreateAsync(resident, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(resident, ApplicationRoles.Resident)).Succeeded);
        ResidentId = resident.Id;

        var membership = new ResidentMembership(
            Guid.NewGuid(), building.Id, unit.Id, resident.Id, DateTimeOffset.UtcNow);
        dbContext.ResidentMemberships.Add(membership);
        MembershipId = membership.Id;

        var admin = new UserAccount(Guid.NewGuid(), AdminEmail, "Admin Test Administrator");
        Assert.True((await userManager.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await userManager.AddToRolesAsync(
            admin, [ApplicationRoles.Administrator, ApplicationRoles.Resident])).Succeeded);
        AdminId = admin.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public virtual ValueTask DisposeAsync()
    {
        Factory.Dispose();
        return ValueTask.CompletedTask;
    }

    protected static Amenity NewAmenity(Guid buildingId, string name, AmenityKind kind)
    {
        var amenity = new Amenity(
            Guid.NewGuid(), buildingId, name, kind, allowsSharedUse: true, allowsExclusiveUse: true);

        for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
        {
            amenity.AddAvailabilityWindow(Guid.NewGuid(), day, new TimeOnly(9, 0), new TimeOnly(22, 0));
        }

        return amenity;
    }

    private static PriceRule Rule(
        Guid buildingId,
        Guid amenityId,
        PriceComponentType component,
        ReservationUseType useType,
        decimal amount,
        DateTimeOffset from) =>
        new(Guid.NewGuid(), buildingId, amenityId, component, useType, "ARS", amount, from, null);

    // --- HTTP ---------------------------------------------------------------------

    protected async Task<HttpClient> LoginAsync(string email)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true",
            new { email, password = Password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    protected Task<HttpClient> LoginAdminAsync() => LoginAsync(AdminEmail);

    protected Task<HttpClient> LoginResidentAsync() => LoginAsync(ResidentEmail);

    protected static DateTimeOffset LocalToUtc(DateOnly date, int hour, int minute = 0) =>
        new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), BuildingOffset).ToUniversalTime();

    protected async Task<Guid> CreateReservationAsync(
        HttpClient resident,
        DateOnly date,
        int startHour = 10,
        int endHour = 11,
        Guid? amenityId = null,
        string useType = "SharedLeisure")
    {
        var response = await resident.PostAsJsonAsync(
            "/api/reservations",
            new
            {
                buildingId = BuildingId,
                amenityId = amenityId ?? SumId,
                useType,
                startsAtUtc = LocalToUtc(date, startHour),
                endsAtUtc = LocalToUtc(date, endHour)
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(
            Json, TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    protected static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object? body) =>
        client.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);

    protected static Task<HttpResponseMessage> CancelAsync(HttpClient admin, Guid id, string? reason) =>
        PostJsonAsync(admin, $"/api/admin/reservations/{id}/cancel", new { reason });

    protected static Task<HttpResponseMessage> RescheduleAsync(
        HttpClient admin,
        Guid id,
        DateTimeOffset start,
        DateTimeOffset end,
        string? reason = "operational change") =>
        PostJsonAsync(
            admin,
            $"/api/admin/reservations/{id}/reschedule",
            new { startsAtUtc = start, endsAtUtc = end, reason });

    protected static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);

    protected static async Task<JsonElement> GetOkAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    protected static async Task<Guid> DeclareCashAsync(HttpClient resident, Guid reservationId)
    {
        var response = await resident.PostAsync(
            $"/api/reservations/{reservationId}/payments/cash", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("paymentId").GetGuid();
    }

    protected static async Task ConfirmCashAsync(HttpClient admin, Guid paymentId)
    {
        var response = await admin.PostAsync(
            $"/api/payments/{paymentId}/cash/confirm", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- database -----------------------------------------------------------------

    protected async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected Task<Reservation> LoadReservationAsync(Guid id) =>
        WithDbAsync(db => db.Reservations
            .AsNoTracking()
            .Include(r => r.Resources)
            .Include(r => r.PriceLines)
            .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken));

    protected Task<List<AuditLog>> AuditAsync(Guid targetId, AuditAction action) =>
        WithDbAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(log => log.TargetId == targetId && log.Action == action)
            .ToListAsync(TestContext.Current.CancellationToken));

    protected Task SetHoldDeadlineAsync(Guid reservationId, DateTimeOffset deadline) =>
        WithDbAsync(async db =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Reservations\" SET \"ExpiresAtUtc\" = {deadline.ToUniversalTime()} WHERE \"Id\" = {reservationId}",
                TestContext.Current.CancellationToken));

    protected Task ExpireNowAsync() =>
        WithDbAsync(async db => await TestServices
            .Expiration(db, TimeProvider.System)
            .ExpirePastHoldsAsync(TestContext.Current.CancellationToken));
}
