using ResidentialAmenities.Api.Modules.Payments.Domain;

namespace ResidentialAmenities.Api.Modules.Payments.Application;

public enum ProviderOutcome
{
    Approved,
    Pending,
    Rejected,
    Cancelled,
    Unmapped
}

/// <summary>
/// Translates Mercado Pago's order status model into the provider-neutral
/// <see cref="PaymentStatus"/>. Statuses per the current official
/// Orders API docs (checked when this was written): success is exactly
/// <c>processed</c> + <c>accredited</c>; <c>created</c>, <c>processing</c>
/// and <c>action_required</c> (waiting_payment/capture/transfer/retry) are
/// still in flight; <c>failed</c> is a declined attempt; <c>canceled</c> and
/// <c>expired</c> end the order without payment. Anything else
/// (<c>refunded</c>, <c>charged_back</c>, <c>processed</c> with another
/// detail such as <c>partially_refunded</c>) is deliberately left Unmapped:
/// refunds/chargebacks are out of scope for #24 and must not silently alter
/// a payment.
/// </summary>
public static class PaymentStatusMapper
{
    public static ProviderOutcome Map(string? status, string? statusDetail)
    {
        var normalizedStatus = status?.Trim().ToLowerInvariant();
        var normalizedDetail = statusDetail?.Trim().ToLowerInvariant();

        return normalizedStatus switch
        {
            "processed" when normalizedDetail == "accredited" => ProviderOutcome.Approved,
            "created" or "processing" or "action_required" => ProviderOutcome.Pending,
            "failed" => ProviderOutcome.Rejected,
            "canceled" or "cancelled" or "expired" => ProviderOutcome.Cancelled,
            _ => ProviderOutcome.Unmapped
        };
    }
}
