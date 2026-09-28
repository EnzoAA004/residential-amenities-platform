namespace ResidentialAmenities.Api.Modules.Payments.Domain;

/// <summary>
/// One payment attempt for a reservation. The amount and currency are copied
/// from the reservation's historical price snapshot when the attempt is
/// created (never recomputed from current price rules, never client-sent).
///
/// <see cref="IdempotencyKey"/> is generated and persisted BEFORE the
/// provider is called and reused verbatim for every technical retry of the
/// same attempt, so a lost response can never create a second remote order.
/// A genuinely new attempt by the user (after a rejection/cancellation) is a
/// new <see cref="Payment"/> with a new key.
/// </summary>
public sealed class Payment
{
    private Payment()
    {
    }

    public Payment(
        Guid id,
        Guid reservationId,
        PaymentMethod method,
        decimal amount,
        string currency,
        string idempotencyKey,
        string requestedExpirationTime,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Payment id is required.", nameof(id));
        }

        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("Reservation id is required.", nameof(reservationId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        Id = id;
        ReservationId = reservationId;
        Method = method;
        Status = PaymentStatus.Created;
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        IdempotencyKey = idempotencyKey;
        RequestedExpirationTime = requestedExpirationTime;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ReservationId { get; private set; }

    public PaymentMethod Method { get; private set; }

    public PaymentStatus Status { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    /// <summary>Sent as <c>X-Idempotency-Key</c> on every retry of this attempt.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>
    /// The exact ISO-8601 duration sent as the order's <c>expiration_time</c>
    /// on the first attempt. Persisted so a technical retry with the same
    /// idempotency key sends a byte-identical request body (the remaining
    /// hold time would otherwise differ on each retry).
    /// </summary>
    public string RequestedExpirationTime { get; private set; } = string.Empty;

    public string? ProviderOrderId { get; private set; }

    public string? CheckoutUrl { get; private set; }

    public string? ProviderStatus { get; private set; }

    public string? ProviderStatusDetail { get; private set; }

    public PaymentReservationOutcome ReservationOutcome { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    /// <summary>
    /// The external reference sent to the provider and validated on every
    /// reconciliation. It is this payment's own id.
    /// </summary>
    public string ExternalReference => Id.ToString("N");

    public bool RequiresManualReview =>
        ReservationOutcome is PaymentReservationOutcome.ApprovedAfterExpiry
            or PaymentReservationOutcome.ApprovedForCancelledReservation
            or PaymentReservationOutcome.ApprovedForMissingReservation;

    public void AttachProviderOrder(
        string providerOrderId,
        string checkoutUrl,
        string providerStatus,
        string providerStatusDetail,
        DateTimeOffset nowUtc)
    {
        ProviderOrderId = providerOrderId;
        CheckoutUrl = checkoutUrl;
        RecordProviderStatus(providerStatus, providerStatusDetail, nowUtc);

        if (Status == PaymentStatus.Created)
        {
            Status = PaymentStatus.Pending;
        }
    }

    public void RecordProviderStatus(
        string providerStatus,
        string providerStatusDetail,
        DateTimeOffset nowUtc)
    {
        ProviderStatus = providerStatus;
        ProviderStatusDetail = providerStatusDetail;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkPending(DateTimeOffset nowUtc)
    {
        if (Status is PaymentStatus.Created or PaymentStatus.Pending)
        {
            Status = PaymentStatus.Pending;
            UpdatedAtUtc = nowUtc;
        }
    }

    /// <returns>true if this call moved the payment to Approved.</returns>
    public bool MarkApproved(DateTimeOffset nowUtc)
    {
        if (Status == PaymentStatus.Approved)
        {
            return false;
        }

        Status = PaymentStatus.Approved;
        ApprovedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    public void MarkRejected(DateTimeOffset nowUtc)
    {
        if (Status is PaymentStatus.Created or PaymentStatus.Pending)
        {
            Status = PaymentStatus.Rejected;
            UpdatedAtUtc = nowUtc;
        }
    }

    public void MarkCancelled(DateTimeOffset nowUtc)
    {
        if (Status is PaymentStatus.Created or PaymentStatus.Pending)
        {
            Status = PaymentStatus.Cancelled;
            UpdatedAtUtc = nowUtc;
        }
    }

    public void RecordReservationOutcome(PaymentReservationOutcome outcome, DateTimeOffset nowUtc)
    {
        ReservationOutcome = outcome;
        UpdatedAtUtc = nowUtc;
    }
}
