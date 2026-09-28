using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// The explicit application contract Payments uses to talk to Reservations
/// (docs/03-architecture/module-boundaries.md: Payments → Reservations).
/// Payments never reads or writes Reservation tables itself, and nothing
/// here mentions a payment provider: Reservations only ever learns "a
/// trusted payment for this reservation succeeded".
/// </summary>
public interface IReservationPaymentContract
{
    /// <summary>
    /// The reservation's payable state, derived exclusively from its
    /// historical price snapshot (<c>ReservationPriceLine</c>) — never from
    /// current price rules (RB-008).
    /// </summary>
    Task<PayableReservation?> GetPayableReservationAsync(
        Guid reservationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks Reservations to apply <c>Pending → Confirmed</c> after a trusted
    /// payment. Reservations decides whether that is still valid.
    /// </summary>
    Task<ReservationConfirmationOutcome> ConfirmPaidReservationAsync(
        Guid reservationId,
        CancellationToken cancellationToken);
}

public sealed record PayableReservation(
    Guid ReservationId,
    Guid BuildingId,
    ReservationStatus Status,
    DateTimeOffset ExpiresAtUtc,
    decimal TotalAmount,
    string? Currency,
    bool HasSinglePriceCurrency,
    bool HasPriceLines);

public enum ReservationConfirmationOutcome
{
    /// <summary>Transitioned <c>Pending → Confirmed</c> by this call.</summary>
    Confirmed = 0,

    /// <summary>Was already <c>Confirmed</c>; safe no-op (idempotent).</summary>
    AlreadyConfirmed = 1,

    /// <summary>
    /// Expired (or Pending but already past its deadline): not revived. The
    /// resources may already belong to someone else.
    /// </summary>
    RejectedExpired = 2,

    /// <summary>Cancelled: never confirmed.</summary>
    RejectedCancelled = 3,

    NotFound = 4
}
