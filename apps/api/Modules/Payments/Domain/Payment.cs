namespace ResidentialAmenities.Api.Modules.Payments.Domain;

/// <summary>
/// One payment attempt for a reservation, whatever the method. The amount and
/// currency are copied from the reservation's historical price snapshot when
/// the attempt is created (never recomputed from current price rules, never
/// client-sent).
///
/// Only general concepts live on every payment. Method-specific data is
/// nullable and only set by that method's factory:
/// <list type="bullet">
/// <item><see cref="CreateMercadoPago"/>: <see cref="IdempotencyKey"/>,
/// <see cref="RequestedExpirationTime"/>, provider order/checkout/status.
/// The key is generated and persisted BEFORE the provider is called and
/// reused verbatim for every technical retry of the same attempt.</item>
/// <item><see cref="CreateCash"/>: none of those; instead the cash
/// declaration/confirmation facts (who confirmed receipt, and when).</item>
/// </list>
/// A genuinely new attempt by the user (after a rejection/cancellation) is a
/// new <see cref="Payment"/>.
/// </summary>
public sealed class Payment
{
    private Payment()
    {
    }

    private Payment(
        Guid id,
        Guid reservationId,
        Guid buildingId,
        PaymentMethod method,
        PaymentStatus initialStatus,
        decimal amount,
        string currency,
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

        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException("Building id is required.", nameof(buildingId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));
        }

        Id = id;
        ReservationId = reservationId;
        BuildingId = buildingId;
        Method = method;
        Status = initialStatus;
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    /// <summary>A Mercado Pago attempt; starts <c>Created</c> (no provider order yet).</summary>
    public static Payment CreateMercadoPago(
        Guid id,
        Guid reservationId,
        Guid buildingId,
        decimal amount,
        string currency,
        string idempotencyKey,
        string requestedExpirationTime,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(requestedExpirationTime))
        {
            throw new ArgumentException(
                "Requested expiration time is required.",
                nameof(requestedExpirationTime));
        }

        return new Payment(
            id,
            reservationId,
            buildingId,
            PaymentMethod.MercadoPago,
            PaymentStatus.Created,
            amount,
            currency,
            createdAtUtc)
        {
            IdempotencyKey = idempotencyKey,
            RequestedExpirationTime = requestedExpirationTime
        };
    }

    /// <summary>
    /// A cash declaration ("I want to pay this in cash"). It starts
    /// <c>Pending</c>: an authorized person has not confirmed receipt yet.
    /// It says nothing about the money having been received.
    /// </summary>
    public static Payment CreateCash(
        Guid id,
        Guid reservationId,
        Guid buildingId,
        decimal amount,
        string currency,
        DateTimeOffset declaredAtUtc) =>
        new(
            id,
            reservationId,
            buildingId,
            PaymentMethod.Cash,
            PaymentStatus.Pending,
            amount,
            currency,
            declaredAtUtc)
        {
            CashDeclaredAtUtc = declaredAtUtc
        };

    public Guid Id { get; private set; }

    public Guid ReservationId { get; private set; }

    /// <summary>The reservation building, copied when the payment is created (admin building scope).</summary>
    public Guid BuildingId { get; private set; }

    public PaymentMethod Method { get; private set; }

    public PaymentStatus Status { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    /// <summary>Mercado Pago only. Sent as <c>X-Idempotency-Key</c> on every retry of this attempt.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>
    /// Mercado Pago only. The exact ISO-8601 duration sent as the order's
    /// <c>expiration_time</c> on the first attempt. Persisted so a technical
    /// retry with the same idempotency key sends a byte-identical request body
    /// (the remaining hold time would otherwise differ on each retry).
    /// </summary>
    public string? RequestedExpirationTime { get; private set; }

    public string? ProviderOrderId { get; private set; }

    public string? CheckoutUrl { get; private set; }

    public string? ProviderStatus { get; private set; }

    public string? ProviderStatusDetail { get; private set; }

    /// <summary>Cash only: when the resident declared they will pay in cash.</summary>
    public DateTimeOffset? CashDeclaredAtUtc { get; private set; }

    /// <summary>Cash only: when an authorized person confirmed receiving the money.</summary>
    public DateTimeOffset? CashConfirmedAtUtc { get; private set; }

    /// <summary>
    /// Cash only: the authenticated <c>UserAccount.Id</c> who confirmed
    /// receipt. A historical id, not a foreign key (module boundary).
    /// </summary>
    public Guid? CashConfirmedByUserId { get; private set; }

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
        EnsureMercadoPago();

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
        EnsureMercadoPago();

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

    /// <summary>
    /// Cash only: an authorized person confirms they physically received the
    /// money. Idempotent — a repeated confirmation changes nothing and keeps
    /// the original actor and timestamp.
    /// </summary>
    /// <returns>true if this call moved the payment to Approved.</returns>
    public bool ConfirmCashReceived(Guid confirmedByUserId, DateTimeOffset nowUtc)
    {
        if (Method != PaymentMethod.Cash)
        {
            throw new InvalidOperationException("Only a cash payment can be confirmed as cash received.");
        }

        if (confirmedByUserId == Guid.Empty)
        {
            throw new ArgumentException("The confirming user is required.", nameof(confirmedByUserId));
        }

        if (Status != PaymentStatus.Pending)
        {
            // Already Approved (idempotent no-op) or otherwise not confirmable.
            return false;
        }

        CashConfirmedByUserId = confirmedByUserId;
        CashConfirmedAtUtc = nowUtc;
        return MarkApproved(nowUtc);
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

    private void EnsureMercadoPago()
    {
        if (Method != PaymentMethod.MercadoPago)
        {
            throw new InvalidOperationException("Provider order data only applies to Mercado Pago payments.");
        }
    }

    public void RecordReservationOutcome(PaymentReservationOutcome outcome, DateTimeOffset nowUtc)
    {
        ReservationOutcome = outcome;
        UpdatedAtUtc = nowUtc;
    }
}
