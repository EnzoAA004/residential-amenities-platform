using System.Net;
using System.Text.Json;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Administration;

[Collection(DevelopmentSeedCollection.Name)]
public sealed class AdminQueryTests : AdminTestBase
{
    private static readonly DateOnly Base = new(2028, 6, 5);

    private async Task<List<Guid>> CreateManyAsync(HttpClient resident, int count)
    {
        var ids = new List<Guid>();

        for (var index = 0; index < count; index++)
        {
            ids.Add(await CreateReservationAsync(resident, Base.AddDays(index)));
        }

        return ids;
    }

    [Fact]
    public async Task Reservations_ArePaginated_NewestFirst_AndCapped()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var ids = await CreateManyAsync(resident, 5);

        var first = await GetOkAsync(admin, $"/api/admin/reservations?buildingId={BuildingId}&pageSize=2&page=1");
        var second = await GetOkAsync(admin, $"/api/admin/reservations?buildingId={BuildingId}&pageSize=2&page=2");
        var third = await GetOkAsync(admin, $"/api/admin/reservations?buildingId={BuildingId}&pageSize=2&page=3");

        Assert.Equal(5, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.Equal(2, second.GetProperty("items").GetArrayLength());
        Assert.Equal(1, third.GetProperty("items").GetArrayLength());

        var seen = new[] { first, second, third }
            .SelectMany(page => page.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("reservation").GetProperty("reservationId").GetGuid())
            .ToList();
        Assert.Equal(ids.AsEnumerable().Reverse(), seen);

        var capped = await GetOkAsync(admin, $"/api/admin/reservations?buildingId={BuildingId}&pageSize=100000");
        Assert.Equal(100, capped.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Reservations_FilterByStatusTypeMembershipAndTime()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();

        var pending = await CreateReservationAsync(resident, Base);
        var cancelled = await CreateReservationAsync(resident, Base.AddDays(1));
        await CancelAsync(admin, cancelled, "test");
        var exclusive = await CreateReservationAsync(resident, Base.AddDays(2), 10, 11, CourtId, "ExclusiveLeisure");

        var byStatus = await GetOkAsync(admin, $"/api/admin/reservations?buildingId={BuildingId}&status=Cancelled");
        Assert.Equal(cancelled, Assert.Single(Ids(byStatus)));

        var byType = await GetOkAsync(admin, $"/api/admin/reservations?buildingId={BuildingId}&useType=ExclusiveLeisure");
        Assert.Equal(exclusive, Assert.Single(Ids(byType)));

        var byMembership = await GetOkAsync(
            admin, $"/api/admin/reservations?buildingId={BuildingId}&membershipId={MembershipId}");
        Assert.Equal(3, Ids(byMembership).Count);
        var byOtherMembership = await GetOkAsync(
            admin, $"/api/admin/reservations?buildingId={BuildingId}&membershipId={Guid.NewGuid()}");
        Assert.Empty(Ids(byOtherMembership));

        var from = Uri.EscapeDataString(LocalToUtc(Base, 0).ToString("O"));
        var to = Uri.EscapeDataString(LocalToUtc(Base.AddDays(1), 0).ToString("O"));
        var byTime = await GetOkAsync(
            admin, $"/api/admin/reservations?buildingId={BuildingId}&fromUtc={from}&toUtc={to}");
        Assert.Equal(pending, Assert.Single(Ids(byTime)));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/admin/reservations?status=Nope", TestContext.Current.CancellationToken))
            .StatusCode);
    }

    [Fact]
    public async Task Reservations_AreScopedToTheBuilding()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        await CreateManyAsync(resident, 2);

        var other = await GetOkAsync(admin, $"/api/admin/reservations?buildingId={Guid.NewGuid()}");

        Assert.Equal(0, other.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ReservationDetail_ShowsLifecycleResourcesPriceSnapshotAndPayments_WithoutSecrets()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Base);
        var paymentId = await DeclareCashAsync(resident, id);
        await ConfirmCashAsync(admin, paymentId);

        var response = await admin.GetAsync(
            $"/api/admin/reservations/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var detail = JsonSerializer.Deserialize<JsonElement>(raw, Json);

        var reservation = detail.GetProperty("reservation");
        Assert.Equal("Confirmed", reservation.GetProperty("status").GetString());
        Assert.Equal("SharedLeisure", reservation.GetProperty("useType").GetString());
        Assert.Equal(BuildingId, reservation.GetProperty("buildingId").GetGuid());
        Assert.Equal(MembershipId, reservation.GetProperty("createdByMembershipId").GetGuid());
        Assert.Equal(5_000m, reservation.GetProperty("total").GetDecimal());
        Assert.Equal("ARS", reservation.GetProperty("currency").GetString());
        Assert.NotEqual(JsonValueKind.Null, reservation.GetProperty("confirmedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, reservation.GetProperty("cancelledAtUtc").ValueKind);
        Assert.Equal(SumId, Assert.Single(reservation.GetProperty("resources").EnumerateArray())
            .GetProperty("amenityId").GetGuid());
        Assert.Equal(1, detail.GetProperty("priceLines").GetArrayLength());
        Assert.False(detail.GetProperty("requiresFinancialReview").GetBoolean());

        var payment = Assert.Single(detail.GetProperty("payments").EnumerateArray());
        Assert.Equal("Cash", payment.GetProperty("method").GetString());
        Assert.Equal("Approved", payment.GetProperty("status").GetString());
        Assert.Equal("ReservationConfirmed", payment.GetProperty("reservationOutcome").GetString());
        Assert.False(payment.GetProperty("requiresManualReview").GetBoolean());
        Assert.Equal(AdminId, payment.GetProperty("cashConfirmedByUserId").GetGuid());

        AssertNoSecrets(raw);
    }

    [Fact]
    public async Task ReservationDetail_UnknownId_Returns404()
    {
        using var admin = await LoginAdminAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.GetAsync(
                $"/api/admin/reservations/{Guid.NewGuid()}", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ReservationList_IncludesThePaymentSummary()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Base);
        await DeclareCashAsync(resident, id);

        var page = await GetOkAsync(admin, $"/api/admin/reservations?buildingId={BuildingId}");

        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        var payment = Assert.Single(item.GetProperty("payments").EnumerateArray());
        Assert.Equal("Pending", payment.GetProperty("status").GetString());
    }

    // --- payments -----------------------------------------------------------------

    [Fact]
    public async Task Payments_ArePaginatedAndScopedToTheBuilding()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();

        for (var index = 0; index < 3; index++)
        {
            var id = await CreateReservationAsync(resident, Base.AddDays(index));
            await DeclareCashAsync(resident, id);
        }

        var first = await GetOkAsync(admin, $"/api/admin/payments?buildingId={BuildingId}&pageSize=2&page=1");
        var second = await GetOkAsync(admin, $"/api/admin/payments?buildingId={BuildingId}&pageSize=2&page=2");

        Assert.Equal(3, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.All(
            first.GetProperty("items").EnumerateArray(),
            payment => Assert.Equal(BuildingId, payment.GetProperty("buildingId").GetGuid()));

        var other = await GetOkAsync(admin, $"/api/admin/payments?buildingId={Guid.NewGuid()}");
        Assert.Equal(0, other.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Payments_ManualReviewFilter_ListsOnlyPaymentsReceivedForAnInactiveReservation()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();

        // A normal confirmed cash payment...
        var okReservation = await CreateReservationAsync(resident, Base);
        var okPayment = await DeclareCashAsync(resident, okReservation);
        await ConfirmCashAsync(admin, okPayment);

        // ...and cash received after the hold expired.
        var lateReservation = await CreateReservationAsync(resident, Base.AddDays(1));
        var latePayment = await DeclareCashAsync(resident, lateReservation);
        await SetHoldDeadlineAsync(lateReservation, DateTimeOffset.UtcNow.AddMinutes(-1));
        await ExpireNowAsync();
        await ConfirmCashAsync(admin, latePayment);

        var review = await GetOkAsync(
            admin, $"/api/admin/payments?buildingId={BuildingId}&requiresManualReview=true");
        var flagged = Assert.Single(review.GetProperty("items").EnumerateArray());
        Assert.Equal(latePayment, flagged.GetProperty("paymentId").GetGuid());
        Assert.Equal("ApprovedAfterExpiry", flagged.GetProperty("reservationOutcome").GetString());
        Assert.True(flagged.GetProperty("requiresManualReview").GetBoolean());

        var rest = await GetOkAsync(
            admin, $"/api/admin/payments?buildingId={BuildingId}&requiresManualReview=false");
        Assert.Equal(okPayment, Assert.Single(rest.GetProperty("items").EnumerateArray())
            .GetProperty("paymentId").GetGuid());

        // The reservation detail flags it for the operator too.
        var detail = await GetOkAsync(admin, $"/api/admin/reservations/{lateReservation}");
        Assert.True(detail.GetProperty("requiresFinancialReview").GetBoolean());
    }

    [Fact]
    public async Task Payments_FilterByMethodStatusAndReservation_AndExposeNoSecrets()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var one = await CreateReservationAsync(resident, Base);
        var two = await CreateReservationAsync(resident, Base.AddDays(1));
        var paidPayment = await DeclareCashAsync(resident, one);
        await DeclareCashAsync(resident, two);
        await ConfirmCashAsync(admin, paidPayment);

        var approved = await GetOkAsync(
            admin, $"/api/admin/payments?buildingId={BuildingId}&status=Approved&method=Cash");
        Assert.Equal(paidPayment, Assert.Single(approved.GetProperty("items").EnumerateArray())
            .GetProperty("paymentId").GetGuid());

        var mercadoPago = await GetOkAsync(
            admin, $"/api/admin/payments?buildingId={BuildingId}&method=MercadoPago");
        Assert.Equal(0, mercadoPago.GetProperty("totalCount").GetInt32());

        var byReservation = await admin.GetAsync(
            $"/api/admin/payments?buildingId={BuildingId}&reservationId={two}",
            TestContext.Current.CancellationToken);
        var raw = await byReservation.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, JsonSerializer.Deserialize<JsonElement>(raw, Json).GetProperty("totalCount").GetInt32());
        AssertNoSecrets(raw);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/admin/payments?method=Nope", TestContext.Current.CancellationToken))
            .StatusCode);
    }

    private static List<Guid> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("reservation").GetProperty("reservationId").GetGuid())
            .ToList();

    private static void AssertNoSecrets(string raw)
    {
        foreach (var forbidden in new[]
                 {
                     "idempotency", "checkout", "accessToken", "webhookSecret", "providerOrderId",
                     "authorization", "signature", "password"
                 })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }
}
