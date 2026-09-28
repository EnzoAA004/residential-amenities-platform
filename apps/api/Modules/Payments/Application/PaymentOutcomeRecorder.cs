using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Application;

namespace ResidentialAmenities.Api.Modules.Payments.Application;

/// <summary>
/// Records what Reservations decided after a trusted payment (any method),
/// exactly once, together with the audit facts that outcome represents.
///
/// "Exactly once" is enforced in the database: the outcome is written with a
/// conditional <c>UPDATE ... WHERE ReservationOutcome = 'None'</c>. Only the
/// caller whose statement changed the row (1 row affected) writes the audit
/// entries — in the same transaction as the update — so a repeated webhook, a
/// repeated cash confirmation, or two concurrent ones can never duplicate
/// them. If a crash left an approved payment without an outcome, the next
/// attempt completes it (and audits it then).
///
/// The audit entries are recorded here rather than at the Pending → Approved
/// step because their metadata includes what Reservations decided.
/// </summary>
public sealed class PaymentOutcomeRecorder(
    AppDbContext dbContext,
    IReservationPaymentContract reservations,
    IAuditRecorder auditRecorder,
    TimeProvider timeProvider,
    ILogger<PaymentOutcomeRecorder> logger)
{
    /// <returns>true when this call recorded the outcome (and its audit facts).</returns>
    public async Task<bool> RecordAsync(
        Payment payment,
        ReservationConfirmationOutcome result,
        CancellationToken cancellationToken)
    {
        var outcome = PaymentReservationOutcomeMapper.Map(result);
        var paymentId = payment.Id;
        var nowUtc = timeProvider.GetUtcNow();

        var buildingId = (await reservations.GetPayableReservationAsync(
            payment.ReservationId,
            cancellationToken))?.BuildingId;

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var updated = await dbContext.Payments
            .Where(candidate =>
                candidate.Id == paymentId &&
                candidate.ReservationOutcome == PaymentReservationOutcome.None)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.ReservationOutcome, outcome)
                    .SetProperty(candidate => candidate.UpdatedAtUtc, nowUtc),
                cancellationToken);

        if (updated == 1)
        {
            AuditOutcome(payment, outcome, buildingId);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // Bring the tracked entity in line with what the database now says.
        await dbContext.Entry(payment).ReloadAsync(cancellationToken);

        if (updated == 1 && payment.RequiresManualReview)
        {
            logger.LogWarning(
                "Payment {PaymentId} was received but reservation {ReservationId} could " +
                "not be confirmed ({Outcome}); manual review required.",
                payment.Id,
                payment.ReservationId,
                outcome);
        }

        return updated == 1;
    }

    private void AuditOutcome(Payment payment, PaymentReservationOutcome outcome, Guid? buildingId)
    {
        var method = payment.Method.ToString();

        if (payment.Method == PaymentMethod.Cash)
        {
            // The confirming user is recorded on the payment; a cash outcome
            // is always preceded by that confirmation.
            var confirmedMetadata = AuditMetadata.PaymentSettled(
                method,
                payment.ReservationId,
                payment.Amount,
                payment.Currency,
                outcome.ToString());

            auditRecorder.Record(payment.CashConfirmedByUserId is { } actor
                ? AuditRecord.ByUser(
                    actor,
                    AuditAction.CashPaymentConfirmed,
                    AuditTargetType.Payment,
                    payment.Id,
                    buildingId,
                    confirmedMetadata)
                : AuditRecord.BySystem(
                    AuditAction.CashPaymentConfirmed,
                    AuditTargetType.Payment,
                    payment.Id,
                    buildingId,
                    confirmedMetadata));
        }
        else
        {
            auditRecorder.Record(AuditRecord.ByExternalProvider(
                AuditAction.PaymentApproved,
                AuditTargetType.Payment,
                payment.Id,
                buildingId,
                AuditMetadata.PaymentSettled(
                    method,
                    payment.ReservationId,
                    payment.Amount,
                    payment.Currency,
                    outcome.ToString())));
        }

        if (outcome is PaymentReservationOutcome.ApprovedAfterExpiry
            or PaymentReservationOutcome.ApprovedForCancelledReservation
            or PaymentReservationOutcome.ApprovedForMissingReservation)
        {
            var review = AuditMetadata.ManualReview(outcome.ToString(), payment.ReservationId);

            auditRecorder.Record(payment.Method == PaymentMethod.Cash &&
                                 payment.CashConfirmedByUserId is { } reviewActor
                ? AuditRecord.ByUser(
                    reviewActor,
                    AuditAction.PaymentRequiresManualReview,
                    AuditTargetType.Payment,
                    payment.Id,
                    buildingId,
                    review)
                : payment.Method == PaymentMethod.Cash
                    ? AuditRecord.BySystem(
                        AuditAction.PaymentRequiresManualReview,
                        AuditTargetType.Payment,
                        payment.Id,
                        buildingId,
                        review)
                    : AuditRecord.ByExternalProvider(
                        AuditAction.PaymentRequiresManualReview,
                        AuditTargetType.Payment,
                        payment.Id,
                        buildingId,
                        review));
        }
    }
}
