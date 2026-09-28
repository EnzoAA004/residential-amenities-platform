using System.Net;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Administration;

[Collection(DevelopmentSeedCollection.Name)]
public sealed class AdminAuthorizationTests : AdminTestBase
{
    // Every administrative route, with a body where one is needed. The
    // resident is authenticated and even owns a membership in the building,
    // but the Administrator policy must still refuse.
    public static TheoryData<string, string> Routes => new()
    {
        { "GET", "/api/admin/reservations" },
        { "GET", "/api/admin/reservations/{reservation}" },
        { "POST", "/api/admin/reservations/{reservation}/cancel" },
        { "POST", "/api/admin/reservations/{reservation}/reschedule" },
        { "GET", "/api/admin/payments" },
        { "GET", "/api/admin/pricing/rules?buildingId={building}" },
        { "POST", "/api/admin/pricing/rules" },
        { "GET", "/api/admin/amenities/{amenity}/availability?buildingId={building}" },
        { "PUT", "/api/admin/amenities/{amenity}/availability" },
        { "POST", "/api/admin/amenities/{amenity}/unavailable-periods" },
        { "DELETE", "/api/admin/amenities/{amenity}/unavailable-periods/{reservation}?buildingId={building}" },
        { "GET", "/api/admin/buildings/{building}/event-slots" },
        { "POST", "/api/admin/buildings/{building}/event-slots" },
        { "PUT", "/api/admin/event-slots/{reservation}" },
        { "POST", "/api/admin/event-slots/{reservation}/deactivate" },
        { "POST", "/api/admin/event-slots/{reservation}/activate" }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Resident_IsForbiddenFromEveryAdminRoute(string method, string route)
    {
        using var resident = await LoginResidentAsync();
        var reservationId = await CreateReservationAsync(resident, new DateOnly(2028, 1, 10));

        var response = await SendAsync(resident, method, Resolve(route, reservationId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Anonymous_IsUnauthorizedOnEveryAdminRoute(string method, string route)
    {
        using var anonymous = Factory.CreateClient();

        var response = await SendAsync(anonymous, method, Resolve(route, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_IsAllowed()
    {
        using var admin = await LoginAdminAsync();

        var response = await admin.GetAsync(
            $"/api/admin/reservations?buildingId={BuildingId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private string Resolve(string route, Guid reservationId) =>
        route
            .Replace("{reservation}", reservationId.ToString())
            .Replace("{building}", BuildingId.ToString())
            .Replace("{amenity}", SumId.ToString());

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
