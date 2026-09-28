using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Payments.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Reservations.Application;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Payments.Application;

public sealed record CashDeclaration(Guid PaymentId, bool AlreadyDeclared);

/// <summary>
/// Cash payments (RF-015, RF-016, RB-012).
///
/// Declaring cash is NOT receiving cash: it creates a <c>Cash</c> payment in
/// <c>Pending</c> and leaves the reservation <c>Pending</c>. Reservations has
/// no cash-specific state, and the declaration does not extend the hold.
/// Only an authorized actor confirming receipt approves the payment, and then
/// Payments reports "a trusted payment succeeded" through the same
/// <see cref="IReservationPaymentContract.ConfirmPaidReservationAsync"/> used
/// for Mercado Pago; Reservations never learns the method.
///
/// Cash confirmation means the money was physically received, so if the
/// reservation can no longer be confirmed (expired or cancelled) the payment
/// is still recorded <c>Approved</c> with an explicit manual-review outcome
/// and the reservation is never revived.
/// </summary>
public sealed class CashPaymentService(
    AppDbContext dbContext,
    IReservationPaymentContract reservations,
    PaymentOutcomeRecorder outcomeRecorder,
    IAuditRecorder auditRecorder,
    TimeProvider timeProvider)
{
    public async Task<CashDeclaration> DeclareAsync(
        PayableReservation reservation,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();

        if (reservation.Status != ReservationStatus.Pending || reservation.ExpiresAtUtc <= nowUtc)
        {
            throw new PaymentRequestException(
                "Only a Pending reservation whose hold has not expired can be paid.",
                StatusCodes.Status409Conflict);
        }

        if (!reservation.HasPriceLines ||
            reservation.TotalAmount <= 0 ||
            !reservation.HasSinglePriceCurrency ||
            reservation.Currency is null)
        {
            throw new PaymentRequestException(
                "The reservation has no valid, single-currency price snapshot to charge.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var existing = await FindActivePaymentAsync(reservation.ReservationId, cancellationToken);

        if (existing is not null)
        {
            return ResolveExisting(existing);
        }

        var payment = Payment.CreateCash(
            Guid.NewGuid(),
            reservation.ReservationId,
            reservation.TotalAmount,
            reservation.Currency,
            nowUtc);

        dbContext.Payments.Add(payment);

        // Recorded only here, when the declaration is really created; an
        // idempotent repeat returns above without auditing again.
        auditRecorder.Record(AuditRecord.ByUser(
            actorUserId,
            AuditAction.CashPaymentDeclared,
            AuditTargetType.Payment,
            payment.Id,
            reservation.BuildingId,
            AuditMetadata.PaymentInitiated(
                payment.ReservationId,
                payment.Amount,
                payment.Currency,
                payment.Method.ToString())));


        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CashDeclaration(payment.Id, AlreadyDeclared: false);
        }
        catch (DbUpdateException error) when (UniqueViolation.IsUniqueViolation(error))
        {
            // A concurrent request created the single allowed active payment
            // first: reuse it rather than duplicating.
            dbContext.ChangeTracker.Clear();

            var concurrent = await FindActivePaymentAsync(reservation.ReservationId, cancellationToken)
                ?? throw new PaymentRequestException(
                    "Could not declare the cash payment; please retry.",
                    StatusCodes.Status409Conflict);

            return ResolveExisting(concurrent);
        }
    }

    /// <summary>
    /// Confirms the physical receipt of the cash. Idempotent: a repeated call
    /// keeps the original actor and timestamp and produces no second effect.
    /// </summary>
    public async Task<Payment> ConfirmAsync(
        Guid paymentId,
        Guid confirmedByUserId,
        CancellationToken cancellationToken)
    {
        var payment = await ApproveAsync(paymentId, confirmedByUserId, cancellationToken);

        // The outcome is recorded once. If a crash left an Approved payment
        // with outcome None, a repeated confirmation completes it (the
        // Reservations side of the contract is idempotent).
        if (payment.ReservationOutcome == PaymentReservationOutcome.None)
        {
            var result = await reservations.ConfirmPaidReservationAsync(
                payment.ReservationId,
                cancellationToken);

            // Records the outcome once, with its audit facts, atomically.
            await outcomeRecorder.RecordAsync(payment, result, cancellationToken);
        }

        return payment;
    }

    /// <summary>
    /// Pending to Approved under a row lock, so two concurrent confirmations
    /// serialize and the second observes Approved without touching the
    /// original actor and timestamp. No external call happens in this
    /// transaction.
    /// </summary>
    private async Task<Payment> ApproveAsync(
        Guid paymentId,
        Guid confirmedByUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var locked = await dbContext.Payments
            .FromSqlInterpolated(
                $"SELECT * FROM \"Payments\" WHERE \"Id\" = {paymentId} FOR UPDATE")
            .ToListAsync(cancellationToken);

        var payment = locked.SingleOrDefault()
            ?? throw new PaymentRequestException("Payment not found.", StatusCodes.Status404NotFound);

        if (payment.Method != PaymentMethod.Cash)
        {
            throw new PaymentRequestException(
                "Only cash payments are confirmed manually.",
                StatusCodes.Status409Conflict);
        }

        if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.Approved))
        {
            throw new PaymentRequestException(
                $"A {payment.Status} cash payment cannot be confirmed.",
                StatusCodes.Status409Conflict);
        }

        if (payment.ConfirmCashReceived(confirmedByUserId, timeProvider.GetUtcNow()))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return payment;
    }

    private static CashDeclaration ResolveExisting(Payment existing)
    {
        if (existing.Method != PaymentMethod.Cash)
        {
            throw new PaymentRequestException(
                $"This reservation already has an active {existing.Method} payment; payment-method switching is not supported.",
                StatusCodes.Status409Conflict);
        }

        if (existing.Status == PaymentStatus.Approved)
        {
            throw new PaymentRequestException(
                "This reservation already has an approved payment.",
                StatusCodes.Status409Conflict);
        }

        return new CashDeclaration(existing.Id, AlreadyDeclared: true);
    }

    private Task<Payment?> FindActivePaymentAsync(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.ReservationId == reservationId &&
                    (candidate.Status == PaymentStatus.Created ||
                     candidate.Status == PaymentStatus.Pending ||
                     candidate.Status == PaymentStatus.Approved),
                cancellationToken);
}
