using ResidentialAmenities.Api.Modules.Payments.Domain;

namespace ResidentialAmenities.Api.Modules.Payments.Application;

/// <summary>
/// Cross-checks a provider order (fetched server-side) against the local
/// payment before anything is reconciled. A single mismatch means the order
/// must not confirm a reservation.
/// </summary>
public static class PaymentOrderValidator
{
    /// <returns>null when the order matches the payment; otherwise a short reason.</returns>
    public static string? Validate(Payment payment, MercadoPagoOrder order)
    {
        if (!string.Equals(order.Id, payment.ProviderOrderId, StringComparison.Ordinal))
        {
            return "order id mismatch";
        }

        if (!string.Equals(
                order.ExternalReference,
                payment.ExternalReference,
                StringComparison.Ordinal))
        {
            return "external_reference mismatch";
        }

        if (order.TotalAmount != payment.Amount)
        {
            return "amount mismatch";
        }

        if (!string.Equals(order.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return "currency mismatch";
        }

        return null;
    }
}
