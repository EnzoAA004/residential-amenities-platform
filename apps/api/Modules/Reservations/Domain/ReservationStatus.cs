namespace ResidentialAmenities.Api.Modules.Reservations.Domain;

/// <summary>
/// Minimal lifecycle for issue #20. <see cref="Confirmed"/> reservations are
/// created directly (no payment yet exists). Payment-hold states
/// (e.g. PendingPayment/Expired) belong to #23/#24/#25 and are deliberately
/// not added here — adding them now without the hold/expiry logic that
/// gives them meaning would be dead, untested surface.
/// </summary>
public enum ReservationStatus
{
    Confirmed = 0,
    Cancelled = 1
}
