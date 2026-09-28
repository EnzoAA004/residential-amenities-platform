namespace ResidentialAmenities.Api.Modules.Payments.Application;

/// <summary>
/// Abstraction over the two Mercado Pago Orders API calls this module
/// needs. Application/domain code and tests depend on this interface (tests
/// use a fake, never the network); the HTTP implementation lives in
/// Payments.Infrastructure. Provider DTOs stay inside Payments and never
/// reach Reservations.
/// </summary>
public interface IMercadoPagoClient
{
    /// <summary>
    /// <c>POST /v1/orders</c> (Checkout Pro, type=online, processing_mode=manual).
    /// <paramref name="idempotencyKey"/> is sent as <c>X-Idempotency-Key</c>
    /// and MUST be the persisted key of the payment attempt, identical on
    /// every technical retry.
    /// </summary>
    Task<MercadoPagoOrder> CreateOrderAsync(
        CreateMercadoPagoOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary><c>GET /v1/orders/{id}</c>; null when the provider reports 404.</summary>
    Task<MercadoPagoOrder?> GetOrderAsync(
        string orderId,
        CancellationToken cancellationToken);
}

/// <param name="ExpirationTime">ISO-8601 duration, e.g. <c>PT29M59S</c>.</param>
public sealed record CreateMercadoPagoOrderRequest(
    string ExternalReference,
    decimal TotalAmount,
    string Description,
    string ExpirationTime);

/// <summary>
/// The subset of an order this module reads. <c>Currency</c> comes from the
/// provider (it follows the seller's country and is not sent on creation),
/// so it is validated against the local snapshot rather than trusted.
/// </summary>
public sealed record MercadoPagoOrder(
    string Id,
    string Status,
    string StatusDetail,
    string ExternalReference,
    decimal TotalAmount,
    string Currency,
    string? CheckoutUrl);

/// <summary>The provider could not be reached or returned an unusable answer.</summary>
public sealed class MercadoPagoUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
