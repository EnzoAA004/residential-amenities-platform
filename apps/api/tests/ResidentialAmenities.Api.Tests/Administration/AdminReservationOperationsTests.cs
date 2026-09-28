using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Application;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using ResidentialAmenities.Api.Tests.Infrastructure;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Administration;

[Collection(DevelopmentSeedCollection.Name)]
public sealed class AdminReservationOperationsTests : AdminTestBase
{
    private static readonly DateOnly Day = new(2028, 2, 14);

    // --- cancel -------------------------------------------------------------------

    [Fact]
    public async Task Cancel_Pending_CancelsRecordsReasonAndTimestamp_AndAuditsOnce()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);

        var response = await CancelAsync(admin, id, "  Building closed for repairs  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reservation = await LoadReservationAsync(id);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.InRange(reservation.CancelledAtUtc!.Value, before, DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal("Building closed for repairs", reservation.CancellationReason);

        var entry = Assert.Single(await AuditAsync(id, AuditAction.ReservationCancelled));
        Assert.Equal(AuditActorType.User, entry.ActorType);
        Assert.Equal(AdminId, entry.ActorUserId);
        Assert.Equal(BuildingId, entry.BuildingId);
        using var metadata = JsonDocument.Parse(entry.MetadataJson!);
        Assert.Equal("Building closed for repairs", metadata.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Cancel_Confirmed_Cancels_AndTheApprovedPaymentIsUntouched_ButFlaggedForFinancialReview()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);
        var paymentId = await DeclareCashAsync(resident, id);
        await ConfirmCashAsync(admin, paymentId);
        Assert.Equal(ReservationStatus.Confirmed, (await LoadReservationAsync(id)).Status);

        var response = await CancelAsync(admin, id, "Requested by the resident");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReservationStatus.Cancelled, (await LoadReservationAsync(id)).Status);

        // No refund, no state change on the payment: money is not touched.
        var payment = await WithDbAsync(db => db.Payments.AsNoTracking()
            .SingleAsync(p => p.Id == paymentId, TestContext.Current.CancellationToken));
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(PaymentReservationOutcome.ReservationConfirmed, payment.ReservationOutcome);

        // The admin read model makes the situation visible.
        var detail = await ReadAsync(response);
        Assert.True(detail.GetProperty("requiresFinancialReview").GetBoolean());
        Assert.Equal("Approved", detail.GetProperty("payments")[0].GetProperty("status").GetString());
        Assert.Equal("Cancelled", detail.GetProperty("reservation").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancel_Expired_IsRejected_AndNothingChanges()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);
        await SetHoldDeadlineAsync(id, DateTimeOffset.UtcNow.AddMinutes(-1));
        await ExpireNowAsync();
        Assert.Equal(ReservationStatus.Expired, (await LoadReservationAsync(id)).Status);

        var response = await CancelAsync(admin, id, "too late");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var reservation = await LoadReservationAsync(id);
        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Null(reservation.CancelledAtUtc);
        Assert.Empty(await AuditAsync(id, AuditAction.ReservationCancelled));
    }

    [Fact]
    public async Task Cancel_Twice_IsIdempotent_KeepsTheOriginalFacts_AndAuditsOnce()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);

        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(admin, id, "first reason")).StatusCode);
        var first = await LoadReservationAsync(id);

        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(admin, id, "second reason")).StatusCode);
        var second = await LoadReservationAsync(id);

        Assert.Equal(first.CancelledAtUtc, second.CancelledAtUtc);
        Assert.Equal("first reason", second.CancellationReason);
        Assert.Single(await AuditAsync(id, AuditAction.ReservationCancelled));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task Cancel_WithoutAReason_Returns400_AndChangesNothing(string? reason)
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);

        var response = await CancelAsync(admin, id, reason);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(id)).Status);
    }

    [Fact]
    public async Task Cancel_ReasonLongerThanTheMaximum_IsRejected_AndExactlyTheMaximumIsAccepted()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await CancelAsync(admin, id, new string('x', 501))).StatusCode);
        Assert.Equal(ReservationStatus.Pending, (await LoadReservationAsync(id)).Status);

        Assert.Equal(
            HttpStatusCode.OK,
            (await CancelAsync(admin, id, new string('x', 500))).StatusCode);
    }

    [Fact]
    public async Task Cancel_TakesTheActorFromTheSession_NotFromTheRequest()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);

        var response = await PostJsonAsync(
            admin,
            $"/api/admin/reservations/{id}/cancel",
            new { reason = "ok", actorUserId = Guid.NewGuid(), role = "Administrator" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AdminId, Assert.Single(await AuditAsync(id, AuditAction.ReservationCancelled)).ActorUserId);
    }

    [Fact]
    public async Task Cancel_UnknownReservation_Returns404()
    {
        using var admin = await LoginAdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(admin, Guid.NewGuid(), "x")).StatusCode);
    }

    [Fact]
    public async Task Cancel_WhenTheAuditWriteFails_TheCancellationRollsBackToo()
    {
        using var resident = await LoginResidentAsync();
        var id = await CreateReservationAsync(resident, Day);

        await WithDbAsync(async db =>
        {
            var clock = TimeProvider.System;
            var service = new ReservationAdminService(
                db, clock, new PoisonedAuditRecorder(db, clock), new ReservationScheduleValidator(db));

            await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                service.CancelAsync(id, "will not stick", AdminId, TestContext.Current.CancellationToken));
            return 0;
        });

        var reservation = await LoadReservationAsync(id);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Null(reservation.CancelledAtUtc);
        Assert.Null(reservation.CancellationReason);
        Assert.Empty(await AuditAsync(id, AuditAction.ReservationCancelled));
    }

    // --- reschedule ---------------------------------------------------------------

    [Fact]
    public async Task Reschedule_ActivePending_MovesOnlyTheRange_AndKeepsHoldResourcesAndPrice()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);
        var before = await LoadReservationAsync(id);

        var newStart = LocalToUtc(Day, 15);
        var newEnd = LocalToUtc(Day, 17);
        var response = await RescheduleAsync(admin, id, newStart, newEnd, "  resident asked to move  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await LoadReservationAsync(id);
        Assert.Equal(newStart, after.StartsAtUtc);
        Assert.Equal(newEnd, after.EndsAtUtc);
        Assert.Equal(ReservationStatus.Pending, after.Status);
        Assert.Equal(before.ExpiresAtUtc, after.ExpiresAtUtc);
        Assert.Equal(before.BuildingId, after.BuildingId);
        Assert.Equal(before.UseType, after.UseType);
        Assert.Equal(
            before.Resources.Select(r => (r.AmenityId, r.IsExclusive)).OrderBy(r => r.AmenityId),
            after.Resources.Select(r => (r.AmenityId, r.IsExclusive)).OrderBy(r => r.AmenityId));
        Assert.Equal(
            before.PriceLines.Select(l => (l.PriceRuleId, l.Amount, l.Currency, l.QuotedAtUtc)),
            after.PriceLines.Select(l => (l.PriceRuleId, l.Amount, l.Currency, l.QuotedAtUtc)));

        var entry = Assert.Single(await AuditAsync(id, AuditAction.ReservationRescheduled));
        Assert.Equal(AdminId, entry.ActorUserId);
        using var metadata = JsonDocument.Parse(entry.MetadataJson!);
        var root = metadata.RootElement;
        Assert.Equal(before.StartsAtUtc, root.GetProperty("previousStartsAtUtc").GetDateTimeOffset());
        Assert.Equal(before.EndsAtUtc, root.GetProperty("previousEndsAtUtc").GetDateTimeOffset());
        Assert.Equal(newStart, root.GetProperty("newStartsAtUtc").GetDateTimeOffset());
        Assert.Equal(newEnd, root.GetProperty("newEndsAtUtc").GetDateTimeOffset());
        Assert.Equal("resident asked to move", root.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Reschedule_PendingWhoseHoldPassed_IsRejected()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);
        await SetHoldDeadlineAsync(id, DateTimeOffset.UtcNow.AddMinutes(-1)); // job has not run yet

        var response = await RescheduleAsync(admin, id, LocalToUtc(Day, 15), LocalToUtc(Day, 16));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(LocalToUtc(Day, 10), (await LoadReservationAsync(id)).StartsAtUtc);
    }

    [Fact]
    public async Task Reschedule_Confirmed_IsAllowed()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);
        await ConfirmCashAsync(admin, await DeclareCashAsync(resident, id));

        var response = await RescheduleAsync(admin, id, LocalToUtc(Day, 12), LocalToUtc(Day, 13));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await LoadReservationAsync(id);
        Assert.Equal(ReservationStatus.Confirmed, after.Status);
        Assert.Equal(LocalToUtc(Day, 12), after.StartsAtUtc);
    }

    [Fact]
    public async Task Reschedule_Expired_IsRejected()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);
        await SetHoldDeadlineAsync(id, DateTimeOffset.UtcNow.AddMinutes(-1));
        await ExpireNowAsync();

        var response = await RescheduleAsync(admin, id, LocalToUtc(Day, 15), LocalToUtc(Day, 16));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ReservationStatus.Expired, (await LoadReservationAsync(id)).Status);
    }

    [Fact]
    public async Task Reschedule_Cancelled_IsRejected()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);
        await CancelAsync(admin, id, "cancelled first");

        var response = await RescheduleAsync(admin, id, LocalToUtc(Day, 15), LocalToUtc(Day, 16));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ReservationStatus.Cancelled, (await LoadReservationAsync(id)).Status);
    }

    [Fact]
    public async Task Reschedule_InvalidRange_IsRejected()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);

        var response = await RescheduleAsync(admin, id, LocalToUtc(Day, 15), LocalToUtc(Day, 14));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Reschedule_WithoutAReason_Returns400(string? reason)
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);

        var response = await RescheduleAsync(admin, id, LocalToUtc(Day, 15), LocalToUtc(Day, 16), reason);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LocalToUtc(Day, 10), (await LoadReservationAsync(id)).StartsAtUtc);
    }

    [Fact]
    public async Task Reschedule_OutsideTheAmenityAvailability_IsRejected()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);

        var response = await RescheduleAsync(admin, id, LocalToUtc(Day, 6), LocalToUtc(Day, 7));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(LocalToUtc(Day, 10), (await LoadReservationAsync(id)).StartsAtUtc);
    }

    [Fact]
    public async Task Reschedule_IntoAMaintenancePeriod_IsRejected()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);

        var period = await PostJsonAsync(
            admin,
            $"/api/admin/amenities/{SumId}/unavailable-periods",
            new
            {
                buildingId = BuildingId,
                startsAtUtc = LocalToUtc(Day, 15),
                endsAtUtc = LocalToUtc(Day, 18),
                reason = "painting"
            });
        Assert.Equal(HttpStatusCode.Created, period.StatusCode);

        var response = await RescheduleAsync(admin, id, LocalToUtc(Day, 16), LocalToUtc(Day, 17));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_IntoAConflict_Returns409_ButNeverConflictsWithItself()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();

        var first = await CreateReservationAsync(
            resident, Day, 10, 11, CourtId, "ExclusiveLeisure");
        var second = await CreateReservationAsync(
            resident, Day, 13, 14, CourtId, "ExclusiveLeisure");

        var conflict = await RescheduleAsync(admin, second, LocalToUtc(Day, 10), LocalToUtc(Day, 11));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(LocalToUtc(Day, 13), (await LoadReservationAsync(second)).StartsAtUtc);

        // Overlapping its own current range is fine: it is excluded from the check.
        var self = await RescheduleAsync(admin, first, LocalToUtc(Day, 10, 30), LocalToUtc(Day, 11, 30));
        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
    }

    [Fact]
    public async Task Reschedule_ToTheSameRange_ChangesNothingAndAuditsNothing()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day);

        var response = await RescheduleAsync(admin, id, LocalToUtc(Day, 10), LocalToUtc(Day, 11));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await AuditAsync(id, AuditAction.ReservationRescheduled));
    }

    [Fact]
    public async Task Reschedule_Event_MustMatchAnActiveSlot_AndKeepsItsPrice()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day, 14, 19, SumId, "Event");
        var priceBefore = (await LoadReservationAsync(id)).PriceLines.Sum(line => line.Amount);

        var offSlot = await RescheduleAsync(admin, id, LocalToUtc(Day, 15), LocalToUtc(Day, 18));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, offSlot.StatusCode);

        var toEvening = await RescheduleAsync(admin, id, LocalToUtc(Day, 19), LocalToUtc(Day, 22));
        Assert.Equal(HttpStatusCode.OK, toEvening.StatusCode);

        var after = await LoadReservationAsync(id);
        Assert.Equal(LocalToUtc(Day, 19), after.StartsAtUtc);
        Assert.Equal(priceBefore, after.PriceLines.Sum(line => line.Amount));
    }

    [Fact]
    public async Task Reschedule_UnknownReservation_Returns404()
    {
        using var admin = await LoginAdminAsync();

        var response = await RescheduleAsync(admin, Guid.NewGuid(), LocalToUtc(Day, 15), LocalToUtc(Day, 16));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentReschedulesIntoTheSameExclusiveSlot_LetExactlyOneWin()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();

        var a = await CreateReservationAsync(resident, Day, 10, 11, CourtId, "ExclusiveLeisure");
        var b = await CreateReservationAsync(resident, Day, 12, 13, CourtId, "ExclusiveLeisure");

        var responses = await Task.WhenAll(
            RescheduleAsync(admin, a, LocalToUtc(Day, 16), LocalToUtc(Day, 17)),
            RescheduleAsync(admin, b, LocalToUtc(Day, 16), LocalToUtc(Day, 17)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        var moved = new[] { await LoadReservationAsync(a), await LoadReservationAsync(b) }
            .Where(reservation => reservation.StartsAtUtc == LocalToUtc(Day, 16))
            .ToList();
        Assert.Single(moved);

        // The invariant: no two exclusive bookings of the court overlap.
        var all = new[] { await LoadReservationAsync(a), await LoadReservationAsync(b) }
            .OrderBy(reservation => reservation.StartsAtUtc)
            .ToList();
        Assert.True(all[0].EndsAtUtc <= all[1].StartsAtUtc);

        Assert.Equal(1, (await AuditAsync(a, AuditAction.ReservationRescheduled)).Count +
                        (await AuditAsync(b, AuditAction.ReservationRescheduled)).Count);
    }

    [Fact]
    public async Task ConcurrentReschedulesOfTheSameReservation_LeaveOneConsistentRange()
    {
        using var resident = await LoginResidentAsync();
        using var admin = await LoginAdminAsync();
        var id = await CreateReservationAsync(resident, Day, 10, 11, CourtId, "ExclusiveLeisure");

        var responses = await Task.WhenAll(
            RescheduleAsync(admin, id, LocalToUtc(Day, 14), LocalToUtc(Day, 15)),
            RescheduleAsync(admin, id, LocalToUtc(Day, 17), LocalToUtc(Day, 18)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var final = await LoadReservationAsync(id);
        Assert.Contains(final.StartsAtUtc, new[] { LocalToUtc(Day, 14), LocalToUtc(Day, 17) });
        Assert.Equal(final.StartsAtUtc.AddHours(1), final.EndsAtUtc);
        Assert.Equal(2, (await AuditAsync(id, AuditAction.ReservationRescheduled)).Count);
    }

    [Fact]
    public async Task Reschedule_WhenTheAuditWriteFails_TheMoveRollsBackToo()
    {
        using var resident = await LoginResidentAsync();
        var id = await CreateReservationAsync(resident, Day);
        var original = await LoadReservationAsync(id);

        await WithDbAsync(async db =>
        {
            var clock = TimeProvider.System;
            var service = new ReservationAdminService(
                db, clock, new PoisonedAuditRecorder(db, clock), new ReservationScheduleValidator(db));

            await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                service.RescheduleAsync(
                    id, LocalToUtc(Day, 15), LocalToUtc(Day, 16), "will not stick", AdminId,
                    TestContext.Current.CancellationToken));
            return 0;
        });

        var after = await LoadReservationAsync(id);
        Assert.Equal(original.StartsAtUtc, after.StartsAtUtc);
        Assert.Equal(original.EndsAtUtc, after.EndsAtUtc);
        Assert.Empty(await AuditAsync(id, AuditAction.ReservationRescheduled));
    }
}
