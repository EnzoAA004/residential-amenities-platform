using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Reservations;

public sealed class ReservationHoldTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2027, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt =
        CreatedAt.AddMinutes(30);

    [Fact]
    public void NewReservation_StartsAsPending()
    {
        var reservation = CreateReservation();

        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Equal(ExpiresAt, reservation.ExpiresAtUtc);
        Assert.Null(reservation.ExpiredAtUtc);
    }

    [Fact]
    public void ExpiresAtUtc_NotAfterCreatedAtUtc_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Reservation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReservationUseType.SharedLeisure,
            CreatedAt,
            CreatedAt.AddHours(1),
            CreatedAt,
            CreatedAt)); // expiresAtUtc == createdAtUtc
    }

    [Fact]
    public void Expire_AfterDeadline_TransitionsToExpired()
    {
        var reservation = CreateReservation();

        reservation.Expire(ExpiresAt.AddSeconds(1));

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Equal(ExpiresAt.AddSeconds(1), reservation.ExpiredAtUtc);
    }

    [Fact]
    public void Expire_BeforeDeadline_DoesNothing()
    {
        var reservation = CreateReservation();

        reservation.Expire(ExpiresAt.AddSeconds(-1));

        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Null(reservation.ExpiredAtUtc);
    }

    [Fact]
    public void Expire_CalledTwice_IsIdempotent()
    {
        var reservation = CreateReservation();
        var firstExpiryInstant = ExpiresAt.AddMinutes(1);

        reservation.Expire(firstExpiryInstant);
        reservation.Expire(firstExpiryInstant.AddMinutes(5));

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        // The second call is a no-op: it must not "re-expire" with a later
        // timestamp once already expired.
        Assert.Equal(firstExpiryInstant, reservation.ExpiredAtUtc);
    }

    [Fact]
    public void Expire_OnConfirmedReservation_NeverTouchesIt()
    {
        var reservation = CreateReservation();
        reservation.Cancel(ExpiresAt.AddDays(1));

        reservation.Expire(ExpiresAt.AddDays(2));

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Null(reservation.ExpiredAtUtc);
    }

    private static Reservation CreateReservation() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReservationUseType.SharedLeisure,
            CreatedAt,
            CreatedAt.AddHours(1),
            CreatedAt,
            ExpiresAt);
}
