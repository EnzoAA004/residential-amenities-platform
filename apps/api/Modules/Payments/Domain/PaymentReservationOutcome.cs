namespace ResidentialAmenities.Api.Modules.Payments.Domain;

/// <summary>
/// What Reservations decided when an approved payment was reported to it.
/// This is what makes "the provider took the money but the reservation could
/// not be confirmed" an explicit, queryable state rather than a silent gap.
/// </summary>
public enum PaymentReservationOutcome
{
    /// <summary>Not applicable yet (payment not approved).</summary>
    None = 0,

    /// <summary>Reservation went (or already was) Confirmed.</summary>
    ReservationConfirmed = 1,

    /// <summary>
    /// Approved after the hold expired. The reservation stays Expired (its
    /// resources may belong to someone else); the money needs manual
    /// review/compensation (refund is a later issue).
    /// </summary>
    ApprovedAfterExpiry = 2,

    /// <summary>Approved for a reservation that was already Cancelled; needs manual review.</summary>
    ApprovedForCancelledReservation = 3,

    /// <summary>The referenced reservation no longer exists; needs manual review.</summary>
    ApprovedForMissingReservation = 4
}
