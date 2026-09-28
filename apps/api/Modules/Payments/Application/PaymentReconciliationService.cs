using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Payments.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Reservations.Application;

namespace ResidentialAmenities.Api.Modules.Payments.Application;

public enum NotificationProcessingOutcome
{
    Processed,
    Duplicate,
    UnknownOrder,
    Mismatch
}

/// <summary>
/// Processes a signature-VERIFIED Mercado Pago notification (RF-014, RB-011,
/// RNF-006). The notification itself is only a trigger: the order id comes
/// from the signed query parameter, and the truth is always the order
/// re-fetched server-side with the private access token.
///
/// Idempotency has two layers. (1) <see cref="PaymentProviderEvent"/> has a
/// unique (Provider, ProviderEventId) key, so a re-delivery of an already
/// processed event returns immediately. (2) Even if a duplicate slips
/// through (or an earlier delivery crashed before being marked processed),
/// every step is state-based and idempotent: the payment can only move to
/// Approved once, and <c>ConfirmPaidReservationAsync</c> treats
/// Confirmed → Confirmed as a no-op — no duplicate business effect exists.
///
/// No DB transaction spans the provider fetch. The payment's approval is
/// committed first; the reservation confirmation (its own row-locked
/// transaction) follows; the resulting outcome is recorded last. A crash in
/// between leaves an Approved payment with outcome <c>None</c>, which the
/// provider's redelivery completes.
/// </summary>
public sealed class PaymentReconciliationService(
    AppDbContext dbContext,
    IMercadoPagoClient mercadoPagoClient,
    IReservationPaymentContract reservations,
    TimeProvider timeProvider,
    ILogger<PaymentReconciliationService> logger)
{
    public const string ProviderName = "MercadoPago";

    public async Task<NotificationProcessingOutcome> ProcessAsync(
        string providerEventId,
        string providerOrderId,
        string eventType,
        CancellationToken cancellationToken)
    {
        var providerEvent = await RegisterEventAsync(
            providerEventId,
            providerOrderId,
            eventType,
            cancellationToken);

        if (providerEvent.IsProcessed)
        {
            return NotificationProcessingOutcome.Duplicate;
        }

        var payment = await dbContext.Payments.SingleOrDefaultAsync(
            candidate => candidate.ProviderOrderId == providerOrderId,
            cancellationToken);

        if (payment is null)
        {
            // Left unprocessed on purpose: a notification could in theory
            // arrive before the provider order id is saved locally, and the
            // redelivery must then be able to reconcile it. Foreign/unknown
            // orders just accumulate harmless identifier-only rows.
            providerEvent.RecordInterimResult(PaymentEventResult.UnknownOrder);
            await dbContext.SaveChangesAsync(cancellationToken);
            return NotificationProcessingOutcome.UnknownOrder;
        }

        // A provider outage propagates: the event stays unprocessed and the
        // endpoint answers non-2xx so Mercado Pago redelivers.
        var order = await mercadoPagoClient.GetOrderAsync(providerOrderId, cancellationToken);

        var mismatch = order is null
            ? "order not found at provider"
            : PaymentOrderValidator.Validate(payment, order);

        if (mismatch is not null)
        {
            logger.LogWarning(
                "Mercado Pago order {OrderId} did not match payment {PaymentId}: {Reason}. " +
                "No state was changed.",
                providerOrderId,
                payment.Id,
                mismatch);

            await CompleteEventAsync(providerEvent, PaymentEventResult.Mismatch, cancellationToken);
            return NotificationProcessingOutcome.Mismatch;
        }

        var nowUtc = timeProvider.GetUtcNow();
        var outcome = PaymentStatusMapper.Map(order!.Status, order.StatusDetail);

        // An approved payment is final for this module: a late/stale event
        // must not overwrite its provider status or downgrade it.
        if (payment.Status != PaymentStatus.Approved)
        {
            payment.RecordProviderStatus(order.Status, order.StatusDetail, nowUtc);
        }

        switch (outcome)
        {
            case ProviderOutcome.Approved:
                payment.MarkApproved(nowUtc);
                await dbContext.SaveChangesAsync(cancellationToken);
                await ConfirmReservationAsync(payment, cancellationToken);
                break;

            case ProviderOutcome.Pending:
                payment.MarkPending(nowUtc);
                break;

            case ProviderOutcome.Rejected:
                payment.MarkRejected(nowUtc);
                break;

            case ProviderOutcome.Cancelled:
                payment.MarkCancelled(nowUtc);
                break;

            case ProviderOutcome.Unmapped:
                await dbContext.SaveChangesAsync(cancellationToken);
                await CompleteEventAsync(
                    providerEvent,
                    PaymentEventResult.UnmappedStatus,
                    cancellationToken);
                return NotificationProcessingOutcome.Processed;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await CompleteEventAsync(providerEvent, PaymentEventResult.Reconciled, cancellationToken);
        return NotificationProcessingOutcome.Processed;
    }

    private async Task ConfirmReservationAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        // Reservations decides whether Pending -> Confirmed is still valid.
        // Idempotent: calling again for an already-confirmed reservation is
        // a no-op that reports AlreadyConfirmed.
        var result = await reservations.ConfirmPaidReservationAsync(
            payment.ReservationId,
            cancellationToken);

        var reservationOutcome = PaymentReservationOutcomeMapper.Map(result);

        if (reservationOutcome != PaymentReservationOutcome.ReservationConfirmed)
        {
            logger.LogWarning(
                "Payment {PaymentId} was approved by the provider but reservation " +
                "{ReservationId} could not be confirmed ({Outcome}); manual review required.",
                payment.Id,
                payment.ReservationId,
                reservationOutcome);
        }

        payment.RecordReservationOutcome(reservationOutcome, timeProvider.GetUtcNow());
    }

    private async Task<PaymentProviderEvent> RegisterEventAsync(
        string providerEventId,
        string providerOrderId,
        string eventType,
        CancellationToken cancellationToken)
    {
        var providerEvent = new PaymentProviderEvent(
            Guid.NewGuid(),
            ProviderName,
            providerEventId,
            providerOrderId,
            eventType,
            timeProvider.GetUtcNow());

        dbContext.PaymentProviderEvents.Add(providerEvent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return providerEvent;
        }
        catch (DbUpdateException error) when (UniqueViolation.IsUniqueViolation(error))
        {
            // Already received (possibly concurrently). If it finished, the
            // caller stops; if it never finished, processing resumes — safe
            // because every step below is idempotent.
            dbContext.ChangeTracker.Clear();

            return await dbContext.PaymentProviderEvents.SingleAsync(
                candidate =>
                    candidate.Provider == ProviderName &&
                    candidate.ProviderEventId == providerEventId,
                cancellationToken);
        }
    }

    private async Task CompleteEventAsync(
        PaymentProviderEvent providerEvent,
        PaymentEventResult result,
        CancellationToken cancellationToken)
    {
        providerEvent.MarkProcessed(result, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
