namespace ResidentialAmenities.Api.Modules.Payments.Domain;

public enum PaymentEventResult
{
    /// <summary>Received and recorded; processing not finished (e.g. crashed mid-way, or provider fetch failed). Safe to reprocess.</summary>
    Received = 0,
    Reconciled = 1,
    UnknownOrder = 2,
    Mismatch = 3,
    UnmappedStatus = 4
}

/// <summary>
/// Minimal, safe record of a signature-verified provider notification, kept
/// for webhook idempotency and audit. It deliberately stores no payload, no
/// payer data and no signature — only identifiers.
/// </summary>
public sealed class PaymentProviderEvent
{
    private PaymentProviderEvent()
    {
    }

    public PaymentProviderEvent(
        Guid id,
        string provider,
        string providerEventId,
        string providerOrderId,
        string eventType,
        DateTimeOffset receivedAtUtc)
    {
        Id = id;
        Provider = provider;
        ProviderEventId = providerEventId;
        ProviderOrderId = providerOrderId;
        EventType = eventType;
        ReceivedAtUtc = receivedAtUtc;
        ProcessingResult = PaymentEventResult.Received;
    }

    public Guid Id { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string ProviderEventId { get; private set; } = string.Empty;

    public string ProviderOrderId { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public PaymentEventResult ProcessingResult { get; private set; }

    public bool IsProcessed => ProcessedAtUtc is not null;

    /// <summary>Records a result without finishing the event, so a redelivery is processed again.</summary>
    public void RecordInterimResult(PaymentEventResult result)
    {
        ProcessingResult = result;
    }

    public void MarkProcessed(PaymentEventResult result, DateTimeOffset nowUtc)
    {
        ProcessingResult = result;
        ProcessedAtUtc = nowUtc;
    }
}
