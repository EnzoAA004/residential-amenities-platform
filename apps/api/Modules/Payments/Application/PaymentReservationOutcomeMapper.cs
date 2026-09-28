using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Application;

namespace ResidentialAmenities.Api.Modules.Payments.Application;

/// <summary>
/// Translates Reservations' answer to "a trusted payment succeeded" into the
/// outcome recorded on the payment. Shared by every payment method:
/// Reservations never learns whether the money came from Mercado Pago or cash.
/// </summary>
public static class PaymentReservationOutcomeMapper
{
    public static PaymentReservationOutcome Map(ReservationConfirmationOutcome result) =>
        result switch
        {
            ReservationConfirmationOutcome.Confirmed
                or ReservationConfirmationOutcome.AlreadyConfirmed
                => PaymentReservationOutcome.ReservationConfirmed,
            ReservationConfirmationOutcome.RejectedExpired
                => PaymentReservationOutcome.ApprovedAfterExpiry,
            ReservationConfirmationOutcome.RejectedCancelled
                => PaymentReservationOutcome.ApprovedForCancelledReservation,
            _ => PaymentReservationOutcome.ApprovedForMissingReservation
        };
}
