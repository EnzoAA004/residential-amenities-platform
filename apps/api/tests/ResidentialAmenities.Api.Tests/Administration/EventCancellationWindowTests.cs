using System.Net;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Administration;

/// <summary>
/// DEC-014/RB-021 (OQ-011): an Event reservation may be cancelled 24 hours
/// or more before its start; less than 24 hours before, cancellation is
/// rejected. This is the cancellation *window* only — no refund/financial-
/// consequence logic exists (RB-016 remains open), and the window does not
/// apply to Leisure (free per RB-018, no financial cancellation policy at
/// all).
///
/// Reservations here are inserted directly (bypassing <c>POST
/// /api/reservations</c>) because that endpoint requires an exact match to a
/// configured <c>EventSlotDefinition</c>'s fixed time-of-day boundaries,
/// which cannot express "exactly N hours from whenever this test happens to
/// run" — the boundary tests need <c>StartsAtUtc</c> anchored to the real
/// clock at test time, not a fixed time of day. This also means these tests
/// exercise <c>ReservationAdminService.CancelAsync</c> directly through its
/// real HTTP endpoint, just with a hand-placed reservation.
///
/// There is no injectable fake clock for these admin integration tests (they
/// run against the real system clock via <c>TimeProvider.System</c>), so the
/// exact-24-hour boundary is tested by approaching it from both sides with a
/// small safety margin (a few seconds) rather than an exact instant — an
/// exact-equality test against the real clock would be flaky depending on
/// how much time elapses between constructing the reservation and the
/// server evaluating "now".
/// </summary>
[Collection(DevelopmentSeedCollection.Name)]
public sealed class EventCancellationWindowTests : AdminTestBase
{
    [Fact]
    public async Task Cancel_EventReservation_MoreThan24HoursBeforeStart_Succeeds()
    {
        using var admin = await LoginAdminAsync();
        var id = await CreateEventReservationAsync(DateTimeOffset.UtcNow.AddHours(25));

        var response = await CancelAsync(admin, id, "plenty of notice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReservationStatus.Cancelled, (await LoadReservationAsync(id)).Status);
    }

    [Fact]
    public async Task Cancel_EventReservation_AtOrAfterThe24HourBoundary_Succeeds()
    {
        using var admin = await LoginAdminAsync();
        // A few seconds past exactly 24h away: guaranteed to still be >= 24h
        // by the time the server evaluates "now", regardless of test/CI
        // latency.
        var id = await CreateEventReservationAsync(DateTimeOffset.UtcNow.AddHours(24).AddSeconds(5));

        var response = await CancelAsync(admin, id, "right at the boundary, still allowed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReservationStatus.Cancelled, (await LoadReservationAsync(id)).Status);
    }

    [Fact]
    public async Task Cancel_EventReservation_JustBeforeThe24HourBoundary_IsRejected()
    {
        using var admin = await LoginAdminAsync();
        // A few seconds short of 24h away: guaranteed to still be < 24h by
        // the time the server evaluates "now".
        var id = await CreateEventReservationAsync(DateTimeOffset.UtcNow.AddHours(24).AddSeconds(-5));

        var response = await CancelAsync(admin, id, "too close to start");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var reservation = await LoadReservationAsync(id);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Null(reservation.CancelledAtUtc);
    }

    [Fact]
    public async Task Cancel_EventReservation_LessThan24HoursBeforeStart_IsRejected()
    {
        using var admin = await LoginAdminAsync();
        var id = await CreateEventReservationAsync(DateTimeOffset.UtcNow.AddHours(2));

        var response = await CancelAsync(admin, id, "same-day, too late");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(id)).Status);
    }

    [Fact]
    public async Task Cancel_FreeSharedLeisureReservation_IsUnaffectedByTheEventWindow_AndHasNoFinancialSideEffect()
    {
        using var admin = await LoginAdminAsync();

        // Well under 24 hours away — would be rejected for an Event, but
        // SharedLeisure (free, RB-018) has no cancellation-window
        // restriction at all.
        var id = await CreateReservationDirectAsync(
            ReservationUseType.SharedLeisure, DateTimeOffset.UtcNow.AddMinutes(30));

        var response = await CancelAsync(admin, id, "resident changed plans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReservationStatus.Cancelled, (await LoadReservationAsync(id)).Status);

        // Free reservation: no Payment ever existed for it to begin with,
        // so cancelling it has no financial record to touch either way.
        var payments = await WithDbAsync(db => db.Payments
            .Where(payment => payment.ReservationId == id)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(payments);
    }

    private Task<Guid> CreateEventReservationAsync(DateTimeOffset startsAtUtc) =>
        CreateReservationDirectAsync(ReservationUseType.Event, startsAtUtc, 15_000m);

    private Task<Guid> CreateReservationDirectAsync(
        ReservationUseType useType, DateTimeOffset startsAtUtc, decimal amount = 0m) =>
        WithDbAsync(async db =>
        {
            var reservation = new Reservation(
                Guid.NewGuid(),
                BuildingId,
                MembershipId,
                useType,
                startsAtUtc,
                startsAtUtc.AddHours(1),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(30));

            reservation.AddResource(Guid.NewGuid(), SumId, isExclusive: useType != ReservationUseType.SharedLeisure);
            if (amount > 0m)
            {
                reservation.AddPriceLine(
                    Guid.NewGuid(), Guid.NewGuid(), SumId, PriceComponentType.Base, "ARS", amount, DateTimeOffset.UtcNow);
            }

            db.Reservations.Add(reservation);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return reservation.Id;
        });
}
