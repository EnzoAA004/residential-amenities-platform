using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Administration;

public sealed class AdminDomainTests
{
    private static readonly DateTimeOffset Created = new(2028, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expires = Created.AddMinutes(30);
    private static readonly DateTimeOffset Start = Created.AddDays(10);

    private static Reservation NewReservation() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReservationUseType.SharedLeisure,
            Start, Start.AddHours(1), Created, Expires);

    [Fact]
    public void Cancel_Pending_RecordsTimestampAndTrimmedReason()
    {
        var reservation = NewReservation();

        Assert.True(reservation.Cancel(Created.AddMinutes(5), "  because  "));

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(Created.AddMinutes(5), reservation.CancelledAtUtc);
        Assert.Equal("because", reservation.CancellationReason);
    }

    [Fact]
    public void Cancel_Confirmed_IsAllowed()
    {
        var reservation = NewReservation();
        reservation.Confirm(Created.AddMinutes(1));

        Assert.True(reservation.Cancel(Created.AddMinutes(5), "x"));
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
    }

    [Fact]
    public void Cancel_Expired_IsRefused_AndNothingIsRevivedOrOverwritten()
    {
        var reservation = NewReservation();
        reservation.Expire(Expires.AddMinutes(1));

        Assert.False(reservation.Cancel(Expires.AddMinutes(2), "x"));

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Null(reservation.CancelledAtUtc);
        Assert.Null(reservation.CancellationReason);
    }

    [Fact]
    public void Cancel_Twice_KeepsTheFirstTimestampAndReason()
    {
        var reservation = NewReservation();
        reservation.Cancel(Created.AddMinutes(5), "first");

        Assert.False(reservation.Cancel(Created.AddMinutes(9), "second"));

        Assert.Equal(Created.AddMinutes(5), reservation.CancelledAtUtc);
        Assert.Equal("first", reservation.CancellationReason);
    }

    [Fact]
    public void TryReschedule_ChangesOnlyTheRange()
    {
        var reservation = NewReservation();
        reservation.AddPriceLine(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PriceComponentType.Base, "ARS", 100m, Created);

        Assert.True(reservation.TryReschedule(Start.AddDays(1), Start.AddDays(1).AddHours(2), Created.AddMinutes(1)));

        Assert.Equal(Start.AddDays(1), reservation.StartsAtUtc);
        Assert.Equal(Expires, reservation.ExpiresAtUtc);
        Assert.Equal(100m, reservation.PriceLines.Single().Amount);
    }

    [Fact]
    public void TryReschedule_PendingPastItsHold_Expired_Cancelled_AreRefused()
    {
        var past = NewReservation();
        Assert.False(past.TryReschedule(Start.AddDays(1), Start.AddDays(1).AddHours(1), Expires.AddSeconds(1)));

        var expired = NewReservation();
        expired.Expire(Expires.AddMinutes(1));
        Assert.False(expired.TryReschedule(Start.AddDays(1), Start.AddDays(1).AddHours(1), Created));

        var cancelled = NewReservation();
        cancelled.Cancel(Created, "x");
        Assert.False(cancelled.TryReschedule(Start.AddDays(1), Start.AddDays(1).AddHours(1), Created));
    }

    [Fact]
    public void TryReschedule_Confirmed_IsAllowedEvenAfterTheHoldDeadline()
    {
        var reservation = NewReservation();
        reservation.Confirm(Created.AddMinutes(1));

        Assert.True(reservation.TryReschedule(Start.AddDays(1), Start.AddDays(1).AddHours(1), Expires.AddDays(1)));
    }

    [Fact]
    public void TryReschedule_InvalidRange_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            NewReservation().TryReschedule(Start, Start, Created));

    [Fact]
    public void EventSlot_Update_KeepsTheNoOvernightInvariant()
    {
        var slot = new EventSlotDefinition(
            Guid.NewGuid(), Guid.NewGuid(), "Slot", new TimeOnly(14, 0), new TimeOnly(19, 0));

        slot.Update("Renamed", new TimeOnly(15, 0), new TimeOnly(20, 0));

        Assert.Equal("Renamed", slot.Name);
        Assert.Equal(new TimeOnly(15, 0), slot.StartTime);
        Assert.Throws<ArgumentException>(() => slot.Update("x", new TimeOnly(22, 0), new TimeOnly(2, 0)));
        Assert.Throws<ArgumentException>(() => slot.Update(" ", new TimeOnly(10, 0), new TimeOnly(11, 0)));
    }

    [Fact]
    public void EventSlot_DeactivateAndActivate_ReportOnlyRealTransitions_AndStopMatching()
    {
        var slot = new EventSlotDefinition(
            Guid.NewGuid(), Guid.NewGuid(), "Slot", new TimeOnly(14, 0), new TimeOnly(19, 0));

        Assert.True(slot.Deactivate());
        Assert.False(slot.Deactivate());
        Assert.False(slot.Matches(new TimeOnly(14, 0), new TimeOnly(19, 0)));

        Assert.True(slot.Activate());
        Assert.False(slot.Activate());
        Assert.True(slot.Matches(new TimeOnly(14, 0), new TimeOnly(19, 0)));
    }
}
