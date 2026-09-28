namespace ResidentialAmenities.Api.Modules.Payments.Application;

/// <summary>
/// Mercado Pago configuration, bound from the <c>MercadoPago</c> section.
///
/// <see cref="AccessToken"/> and <see cref="WebhookSecret"/> are secrets:
/// they come from User Secrets / environment variables (e.g.
/// <c>MercadoPago__AccessToken</c>), are never committed, never sent to the
/// frontend and never logged. Only their configuration NAMES live in
/// source control.
/// </summary>
public sealed class MercadoPagoOptions
{
    public const string SectionName = "MercadoPago";

    /// <summary>Private access token (secret). Server-side only.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Webhook signing secret (secret). Used to verify <c>x-signature</c>.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://api.mercadopago.com";

    /// <summary>
    /// UX-only browser return URLs. They never confirm a payment (RB-011);
    /// only server-side reconciliation with Mercado Pago does.
    /// </summary>
    public string SuccessUrl { get; set; } = string.Empty;

    public string PendingUrl { get; set; } = string.Empty;

    public string FailureUrl { get; set; } = string.Empty;

    /// <summary>
    /// Maximum age/skew accepted for the <c>x-signature</c> timestamp. The
    /// official SDKs make this optional; replaying an old notification is
    /// harmless here (processing is idempotent and always re-fetches the
    /// order), so this is defence in depth, kept generous because the
    /// provider retries deliveries.
    /// </summary>
    public int WebhookToleranceSeconds { get; set; } = 600;

    public int RequestTimeoutSeconds { get; set; } = 15;
}
