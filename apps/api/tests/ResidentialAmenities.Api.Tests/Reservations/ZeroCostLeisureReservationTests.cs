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
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Reservations;

/// <summary>
/// DEC-014/RB-018: SharedLeisure and ExclusiveLeisure are now free (ARS 0).
/// A zero-cost reservation must reach <c>Confirmed</c> immediately at
/// creation — with no Mercado Pago or cash payment involved, and no
/// synthetic/zero-amount Payment record created for it.
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class ZeroCostLeisureReservationTests : IAsyncLifetime
{
    private const string Password = "Test!Password123";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));

    private string _residentEmail = string.Empty;
    private Guid _buildingId;
    private Guid _amenityId;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _residentEmail = $"free-leisure-{Guid.NewGuid():N}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccount>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var building = new Building(
            Guid.NewGuid(), "Zero Cost Building", "America/Argentina/Buenos_Aires");
        dbContext.Buildings.Add(building);
        _buildingId = building.Id;

        var unit = new Unit(Guid.NewGuid(), building.Id, floor: 1, door: "1", label: "Z1");
        dbContext.Units.Add(unit);

        var amenity = new Amenity(
            Guid.NewGuid(),
            building.Id,
            "Zero Cost Room",
            AmenityKind.Other,
            allowsSharedUse: true,
            allowsExclusiveUse: true);
        dbContext.Amenities.Add(amenity);
        _amenityId = amenity.Id;

        // Open every day of the week, all day — an amenity with no
        // AmenityAvailabilityWindow rows is never available (empty windows
        // means zero open intervals), not unrestricted.
        foreach (var dayOfWeek in Enum.GetValues<DayOfWeek>())
        {
            dbContext.AmenityAvailabilityWindows.Add(new AmenityAvailabilityWindow(
                Guid.NewGuid(),
                amenity.Id,
                dayOfWeek,
                new TimeOnly(0, 0),
                new TimeOnly(23, 59, 59)));
        }

        var nowUtc = DateTimeOffset.UtcNow;

        dbContext.PriceRules.Add(new PriceRule(
            Guid.NewGuid(),
            building.Id,
            amenity.Id,
            PriceComponentType.Base,
            ReservationUseType.SharedLeisure,
            "ARS",
            0m,
            nowUtc.AddDays(-1),
            null));

        dbContext.PriceRules.Add(new PriceRule(
            Guid.NewGuid(),
            building.Id,
            amenity.Id,
            PriceComponentType.Base,
            ReservationUseType.ExclusiveLeisure,
            "ARS",
            0m,
            nowUtc.AddDays(-1),
            null));

        var resident = new UserAccount(Guid.NewGuid(), _residentEmail, "Free Leisure Resident");
        Assert.True((await userManager.CreateAsync(resident, Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(resident, ApplicationRoles.Resident)).Succeeded);
        dbContext.ResidentMemberships.Add(new ResidentMembership(
            Guid.NewGuid(), building.Id, unit.Id, resident.Id, nowUtc));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("SharedLeisure")]
    [InlineData("ExclusiveLeisure")]
    public async Task Create_FreeLeisureReservation_IsConfirmedImmediately_WithNoPayment(string useType)
    {
        using var client = await LoginAsync(_residentEmail);
        var cancellationToken = TestContext.Current.CancellationToken;

        var startsAtUtc = DateTimeOffset.UtcNow.AddDays(1);

        var createResponse = await client.PostAsJsonAsync(
            "/api/reservations",
            new
            {
                buildingId = _buildingId,
                amenityId = _amenityId,
                useType,
                startsAtUtc,
                endsAtUtc = startsAtUtc.AddHours(1)
            },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var body = JsonDocument.Parse(
            await createResponse.Content.ReadAsStringAsync(cancellationToken));

        Assert.Equal("Confirmed", body.RootElement.GetProperty("status").GetString());
        Assert.NotEqual(
            JsonValueKind.Null,
            body.RootElement.GetProperty("confirmedAtUtc").ValueKind);
        Assert.Equal(0m, body.RootElement.GetProperty("totalAmount").GetDecimal());

        var reservationId = body.RootElement.GetProperty("id").GetGuid();

        var paymentsResponse = await client.GetAsync(
            $"/api/reservations/{reservationId}/payments", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, paymentsResponse.StatusCode);

        var payments = await paymentsResponse.Content
            .ReadFromJsonAsync<List<JsonElement>>(JsonOptions, cancellationToken);
        Assert.NotNull(payments);
        Assert.Empty(payments);
    }

    [Fact]
    public async Task DeclareCash_OnFreeReservation_IsRejected()
    {
        using var client = await LoginAsync(_residentEmail);
        var cancellationToken = TestContext.Current.CancellationToken;

        var startsAtUtc = DateTimeOffset.UtcNow.AddDays(2);

        var createResponse = await client.PostAsJsonAsync(
            "/api/reservations",
            new
            {
                buildingId = _buildingId,
                amenityId = _amenityId,
                useType = "SharedLeisure",
                startsAtUtc,
                endsAtUtc = startsAtUtc.AddHours(1)
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var body = JsonDocument.Parse(
            await createResponse.Content.ReadAsStringAsync(cancellationToken));
        var reservationId = body.RootElement.GetProperty("id").GetGuid();

        // The reservation is already Confirmed (free) — attempting to
        // declare cash on it must be rejected, not silently create a
        // zero-amount cash payment.
        var cashResponse = await client.PostAsJsonAsync(
            $"/api/reservations/{reservationId}/payments/cash",
            new { },
            cancellationToken);

        Assert.True(
            cashResponse.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity,
            $"Expected 409 or 422, got {cashResponse.StatusCode}.");
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
