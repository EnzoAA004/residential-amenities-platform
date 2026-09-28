namespace ResidentialAmenities.Api.Modules.Audit.Domain;

/// <summary>
/// The stable catalog of audited actions. Endpoints never invent action
/// strings: adding an audited fact means adding a member here. Stored by name,
/// so members must never be renamed once released.
/// </summary>
public enum AuditAction
{
    // Identity
    AuthenticationSucceeded = 0,
    AuthenticationFailed = 1,
    Logout = 2,

    // Reservations
    ReservationCreated = 10,
    ReservationConfirmed = 11,
    ReservationExpired = 12,

    /// <summary>Prepared for issue #26 (admin cancellation); nothing emits it yet.</summary>
    ReservationCancelled = 13,

    // Payments
    MercadoPagoPaymentInitiated = 20,
    PaymentApproved = 21,
    PaymentRejected = 22,
    PaymentCancelled = 23,
    CashPaymentDeclared = 24,
    CashPaymentConfirmed = 25,
    PaymentRequiresManualReview = 26,

    // External provider
    MercadoPagoWebhookProcessed = 30
}
