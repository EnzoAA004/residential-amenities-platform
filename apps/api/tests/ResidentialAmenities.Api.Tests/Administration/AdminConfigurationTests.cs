using System.Net;
using System.Net.Http.Json;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Administration;

/// <summary>Availability windows, maintenance periods and Event slots.</summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class AdminConfigurationTests : AdminTestBase
{
    // 2028-05-01 is a Monday.
    private static readonly DateOnly Monday = new(2028, 5, 1);
    private static readonly DateOnly Tuesday = new(2028, 5, 2);

    private Task<HttpResponseMessage> ReplaceWindowsAsync(
        HttpClient admin,
        Guid? amenityId,
        object[] windows,
        Guid? buildingId = null) =>
        admin.PutAsJsonAsync(
            $"/api/admin/amenities/{amenityId ?? SumId}/availability",
            new { buildingId = buildingId ?? BuildingId, windows },
            TestContext.Current.CancellationToken);

    private static object Window(DayOfWeek day, string start, string end) =>
        new { dayOfWeek = day, startTime = start, endTime = end };

    // --- availability -------------------------------------------------------------

    [Fact]
    public async Task ReplaceAvailability_Valid_IsReflectedAndShapesFutureBookings()
    {
        using var admin = await LoginAdminAsync();
        using var resident = await LoginResidentAsync();

        var response = await ReplaceWindowsAsync(
            admin, CourtId, [Window(DayOfWeek.Monday, "10:00:00", "12:00:00")]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = await ReadAsync(response);
        var window = Assert.Single(config.GetProperty("windows").EnumerateArray());
        Assert.Equal("10:00:00", window.GetProperty("startTime").GetString());

        // The resident-facing availability query reflects it: Tuesday is closed now.
        var open = await GetOkAsync(
            resident,
            $"/api/amenities/{CourtId}/availability?fromUtc={Uri.EscapeDataString(LocalToUtc(Tuesday, 0).ToString("O"))}" +
            $"&toUtc={Uri.EscapeDataString(LocalToUtc(Tuesday, 23).ToString("O"))}");
        Assert.Equal(0, open.GetArrayLength());

        // A future reservation respects the new configuration.
        Assert.Equal(
            HttpStatusCode.Created,
            (await TryCreateAsync(resident, Monday, 10, 11, CourtId)).StatusCode);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await TryCreateAsync(resident, Monday, 13, 14, CourtId)).StatusCode);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await TryCreateAsync(resident, Tuesday, 10, 11, CourtId)).StatusCode);
    }

    [Fact]
    public async Task ReplaceAvailability_DoesNotChangeExistingReservations()
    {
        using var admin = await LoginAdminAsync();
        using var resident = await LoginResidentAsync();
        var id = await CreateReservationAsync(resident, Tuesday, 15, 16, CourtId);
        var before = await LoadReservationAsync(id);

        var response = await ReplaceWindowsAsync(
            admin, CourtId, [Window(DayOfWeek.Monday, "10:00:00", "12:00:00")]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await LoadReservationAsync(id);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.StartsAtUtc, after.StartsAtUtc);
        Assert.Equal(before.EndsAtUtc, after.EndsAtUtc);
    }

    [Fact]
    public async Task ReplaceAvailability_InvalidRanges_AreRejected_AndConfigurationIsUnchanged()
    {
        using var admin = await LoginAdminAsync();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await ReplaceWindowsAsync(
                admin, CourtId, [Window(DayOfWeek.Monday, "12:00:00", "10:00:00")])).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await ReplaceWindowsAsync(
                admin, CourtId, [Window(DayOfWeek.Monday, "10:00:00", "10:00:00")])).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await ReplaceWindowsAsync(admin, CourtId, [])).StatusCode);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await ReplaceWindowsAsync(
                admin,
                CourtId,
                [Window(DayOfWeek.Monday, "10:00:00", "13:00:00"), Window(DayOfWeek.Monday, "12:00:00", "15:00:00")]))
            .StatusCode);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await ReplaceWindowsAsync(
                admin,
                CourtId,
                [Window(DayOfWeek.Monday, "10:00:00", "12:00:00"), Window(DayOfWeek.Monday, "10:00:00", "12:00:00")]))
            .StatusCode);

        var config = await GetOkAsync(
            admin, $"/api/admin/amenities/{CourtId}/availability?buildingId={BuildingId}");
        Assert.Equal(7, config.GetProperty("windows").GetArrayLength());
        Assert.Empty(await AuditAsync(CourtId, AuditAction.AmenityAvailabilityChanged));
    }

    [Fact]
    public async Task Availability_ForAnAmenityOfAnotherBuilding_IsNotFound()
    {
        using var admin = await LoginAdminAsync();

        var response = await ReplaceWindowsAsync(
            admin,
            DevelopmentDataSeeder.PilotSumId,
            [Window(DayOfWeek.Monday, "10:00:00", "12:00:00")]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceAvailability_AuditsOnce_WithoutSerializingTheWindows()
    {
        using var admin = await LoginAdminAsync();

        await ReplaceWindowsAsync(
            admin, CourtId, [Window(DayOfWeek.Monday, "10:00:00", "12:00:00"), Window(DayOfWeek.Friday, "09:00:00", "20:00:00")]);

        var entry = Assert.Single(await AuditAsync(CourtId, AuditAction.AmenityAvailabilityChanged));
        Assert.Equal(AdminId, entry.ActorUserId);
        Assert.Equal(BuildingId, entry.BuildingId);
        Assert.Equal(AuditTargetType.Amenity, entry.TargetType);
        using var metadata = JsonDocument.Parse(entry.MetadataJson!);
        Assert.Equal("WindowsReplaced", metadata.RootElement.GetProperty("operation").GetString());
        Assert.Equal(2, metadata.RootElement.GetProperty("count").GetInt32());
        Assert.DoesNotContain("10:00", entry.MetadataJson!);
    }

    [Fact]
    public async Task MaintenancePeriod_BlocksBookings_UntilRemoved_AndBothAreAudited()
    {
        using var admin = await LoginAdminAsync();
        using var resident = await LoginResidentAsync();

        var created = await PostJsonAsync(
            admin,
            $"/api/admin/amenities/{CourtId}/unavailable-periods",
            new
            {
                buildingId = BuildingId,
                startsAtUtc = LocalToUtc(Tuesday, 9),
                endsAtUtc = LocalToUtc(Tuesday, 22),
                reason = "resurfacing"
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var periodId = (await ReadAsync(created)).GetProperty("periodId").GetGuid();

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await TryCreateAsync(resident, Tuesday, 10, 11, CourtId)).StatusCode);

        var config = await GetOkAsync(
            admin, $"/api/admin/amenities/{CourtId}/availability?buildingId={BuildingId}");
        Assert.Single(config.GetProperty("unavailablePeriods").EnumerateArray());

        var removed = await admin.DeleteAsync(
            $"/api/admin/amenities/{CourtId}/unavailable-periods/{periodId}?buildingId={BuildingId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        Assert.Equal(
            HttpStatusCode.Created,
            (await TryCreateAsync(resident, Tuesday, 10, 11, CourtId)).StatusCode);

        var entries = await AuditAsync(CourtId, AuditAction.AmenityAvailabilityChanged);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.MetadataJson!.Contains("UnavailablePeriodAdded"));
        Assert.Contains(entries, entry => entry.MetadataJson!.Contains("UnavailablePeriodRemoved"));
    }

    [Fact]
    public async Task MaintenancePeriod_InvalidRange_OrDuplicate_IsRejected()
    {
        using var admin = await LoginAdminAsync();
        var url = $"/api/admin/amenities/{CourtId}/unavailable-periods";

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await PostJsonAsync(admin, url, new
            {
                buildingId = BuildingId,
                startsAtUtc = LocalToUtc(Tuesday, 12),
                endsAtUtc = LocalToUtc(Tuesday, 10)
            })).StatusCode);

        var body = new
        {
            buildingId = BuildingId,
            startsAtUtc = LocalToUtc(Tuesday, 12),
            endsAtUtc = LocalToUtc(Tuesday, 14)
        };
        Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(admin, url, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostJsonAsync(admin, url, body)).StatusCode);
    }

    // --- event slots --------------------------------------------------------------

    [Fact]
    public async Task EventSlots_CreateUpdateDeactivate_AreAuditedWithTypedMetadata()
    {
        using var admin = await LoginAdminAsync();

        var created = await PostJsonAsync(
            admin,
            $"/api/admin/buildings/{BuildingId}/event-slots",
            new { name = "Brunch", startTime = "10:00:00", endTime = "13:00:00" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var slotId = (await ReadAsync(created)).GetProperty("id").GetGuid();

        var createdEntry = Assert.Single(await AuditAsync(slotId, AuditAction.EventSlotCreated));
        Assert.Equal(AdminId, createdEntry.ActorUserId);
        Assert.Equal(BuildingId, createdEntry.BuildingId);

        var updated = await admin.PutAsJsonAsync(
            $"/api/admin/event-slots/{slotId}",
            new { name = "Late brunch", startTime = "11:00:00", endTime = "14:00:00" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedEntry = Assert.Single(await AuditAsync(slotId, AuditAction.EventSlotUpdated));
        using var metadata = JsonDocument.Parse(updatedEntry.MetadataJson!);
        Assert.Equal("10:00", metadata.RootElement.GetProperty("previousStartTime").GetString());
        Assert.Equal("11:00", metadata.RootElement.GetProperty("startTime").GetString());
        Assert.Equal("Late brunch", metadata.RootElement.GetProperty("name").GetString());

        var deactivated = await PostJsonAsync(admin, $"/api/admin/event-slots/{slotId}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.False((await ReadAsync(deactivated)).GetProperty("isActive").GetBoolean());
        Assert.Single(await AuditAsync(slotId, AuditAction.EventSlotDeactivated));

        // Deactivating again is a no-op: nothing more is audited.
        await PostJsonAsync(admin, $"/api/admin/event-slots/{slotId}/deactivate", null);
        Assert.Single(await AuditAsync(slotId, AuditAction.EventSlotDeactivated));
    }

    [Fact]
    public async Task EventSlots_ListShowsThePlaceholdersAndNewSlots()
    {
        using var admin = await LoginAdminAsync();

        var slots = await GetOkAsync(admin, $"/api/admin/buildings/{BuildingId}/event-slots");

        Assert.Equal(2, slots.GetArrayLength());
        Assert.Equal("14:00:00", slots[0].GetProperty("startTime").GetString());
    }

    [Theory]
    [InlineData("12:00:00", "10:00:00")]
    [InlineData("22:00:00", "02:00:00")] // overnight is not supported
    [InlineData("10:00:00", "10:00:00")]
    public async Task EventSlots_InvalidOrOvernightRange_IsRejected(string start, string end)
    {
        using var admin = await LoginAdminAsync();

        var response = await PostJsonAsync(
            admin,
            $"/api/admin/buildings/{BuildingId}/event-slots",
            new { name = "Bad", startTime = start, endTime = end });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EventSlots_DuplicateTimes_AreRejected()
    {
        using var admin = await LoginAdminAsync();

        var response = await PostJsonAsync(
            admin,
            $"/api/admin/buildings/{BuildingId}/event-slots",
            new { name = "Same as afternoon", startTime = "14:00:00", endTime = "19:00:00" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EventSlots_ADeactivatedSlot_StopsAcceptingEventReservations_UntilReactivated()
    {
        using var admin = await LoginAdminAsync();
        using var resident = await LoginResidentAsync();

        var slots = await GetOkAsync(admin, $"/api/admin/buildings/{BuildingId}/event-slots");
        var afternoon = slots.EnumerateArray().Single(slot => slot.GetProperty("startTime").GetString() == "14:00:00");
        var slotId = afternoon.GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.Created,
            (await TryCreateAsync(resident, Monday, 14, 19, SumId, "Event")).StatusCode);

        await PostJsonAsync(admin, $"/api/admin/event-slots/{slotId}/deactivate", null);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await TryCreateAsync(resident, Tuesday, 14, 19, SumId, "Event")).StatusCode);

        await PostJsonAsync(admin, $"/api/admin/event-slots/{slotId}/activate", null);

        Assert.Equal(
            HttpStatusCode.Created,
            (await TryCreateAsync(resident, Tuesday, 14, 19, SumId, "Event")).StatusCode);

        // The reservation made while the slot was active is untouched throughout.
        var existing = await WithDbAsync(db => db.Reservations.AsNoTracking()
            .Where(r => r.BuildingId == BuildingId && r.UseType == ResidentialAmenities.Api.Modules.Pricing.Domain.ReservationUseType.Event)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, existing.Count);
        Assert.All(existing, reservation => Assert.Equal(ReservationStatus.Pending, reservation.Status));
    }

    [Fact]
    public async Task EventSlots_UnknownSlotOrBuilding_Returns404()
    {
        using var admin = await LoginAdminAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.PutAsJsonAsync(
                $"/api/admin/event-slots/{Guid.NewGuid()}",
                new { name = "x", startTime = "10:00:00", endTime = "11:00:00" },
                TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.GetAsync(
                $"/api/admin/buildings/{Guid.NewGuid()}/event-slots",
                TestContext.Current.CancellationToken)).StatusCode);
    }

    // --- helpers ------------------------------------------------------------------

    private Task<HttpResponseMessage> TryCreateAsync(
        HttpClient resident,
        DateOnly date,
        int startHour,
        int endHour,
        Guid amenityId,
        string useType = "SharedLeisure") =>
        PostJsonAsync(
            resident,
            "/api/reservations",
            new
            {
                buildingId = BuildingId,
                amenityId,
                useType,
                startsAtUtc = LocalToUtc(date, startHour),
                endsAtUtc = LocalToUtc(date, endHour)
            });
}
