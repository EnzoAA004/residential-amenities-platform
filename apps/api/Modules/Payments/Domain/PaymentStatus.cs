namespace ResidentialAmenities.Api.Modules.Payments.Domain;

/// <summary>
/// Provider-neutral payment state. Deliberately NOT a copy of Mercado Pago's
/// status strings — those are stored separately as
/// <see cref="Payment.ProviderStatus"/>/<see cref="Payment.ProviderStatusDetail"/>.
/// A Refunded state is intentionally absent: refund logic is out of scope
/// for #24 and a member with no behaviour behind it would be dead surface.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Persisted locally; the provider order does not exist yet (or its creation result is unknown).</summary>
    Created = 0,

    /// <summary>Provider order exists and is awaiting/processing payment.</summary>
    Pending = 1,

    /// <summary>The provider verifiably credited the payment.</summary>
    Approved = 2,

    /// <summary>The provider attempt failed/was declined.</summary>
    Rejected = 3,

    /// <summary>The provider order was cancelled or expired.</summary>
    Cancelled = 4
}
