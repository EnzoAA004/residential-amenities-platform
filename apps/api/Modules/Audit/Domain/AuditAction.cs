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

    /// <summary>An Administrator created a resident account record (issue #93).</summary>
    ResidentInvited = 3,

    /// <summary>A resident verified their code and set their initial password (issue #93).</summary>
    ResidentActivated = 4,

    /// <summary>A password-reset code was issued for an existing account (issue #93).</summary>
    PasswordResetRequested = 5,

    /// <summary>A password was successfully reset via a verified code (issue #93).</summary>
    PasswordResetCompleted = 6,

    // Reservations
    ReservationCreated = 10,
    ReservationConfirmed = 11,
    ReservationExpired = 12,

    /// <summary>Administrative cancellation (issue #26).</summary>
    ReservationCancelled = 13,

    /// <summary>Administrative move to another time range (issue #26).</summary>
    ReservationRescheduled = 14,

    // Payments
    MercadoPagoPaymentInitiated = 20,
    PaymentApproved = 21,
    PaymentRejected = 22,
    PaymentCancelled = 23,
    CashPaymentDeclared = 24,
    CashPaymentConfirmed = 25,
    PaymentRequiresManualReview = 26,

    // Pricing configuration (issue #26)
    PriceRuleCreated = 40,
    PriceRuleSuperseded = 41,

    // Amenity availability configuration (issue #26)
    AmenityAvailabilityChanged = 50,

    // Event slot configuration (issue #26)
    EventSlotCreated = 60,
    EventSlotUpdated = 61,
    EventSlotDeactivated = 62,

    // External provider
    MercadoPagoWebhookProcessed = 30,

    /// <summary>Administrator reviewed/updated an incident report's status (issue #91).</summary>
    IncidentReportStatusChanged = 70
}
