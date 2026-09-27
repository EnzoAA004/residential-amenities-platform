namespace ResidentialAmenities.Api.Modules.Reservations.Domain;

/// <summary>
/// Lifecycle as of issue #23 (RB-009, RB-010). Every reservation is created
/// as <see cref="Pending"/> — the payment hold — with an
/// <see cref="Reservation.ExpiresAtUtc"/>. It blocks resources exactly like
/// <see cref="Confirmed"/> while active. <see cref="Expired"/> is reached
/// only from <see cref="Pending"/>, once <c>ExpiresAtUtc</c> has passed, and
/// no longer blocks anything.
///
/// There is no automated <see cref="Pending"/> → <see cref="Confirmed"/>
/// transition yet: that requires a trusted payment confirmation, which
/// belongs to #24 (Mercado Pago) / #25 (cash) and is intentionally not
/// simulated here.
/// </summary>
public enum ReservationStatus
{
    Confirmed = 0,
    Cancelled = 1,
    Pending = 2,
    Expired = 3
}
