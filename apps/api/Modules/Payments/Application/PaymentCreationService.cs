using System.Xml;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Payments.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Reservations.Application;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Payments.Application;

public sealed record InitiatedPayment(
    Guid PaymentId,
    string ProviderOrderId,
    string CheckoutUrl,
    DateTimeOffset ReservationExpiresAtUtc);

/// <summary>
/// Starts (or resumes) a Mercado Pago payment attempt for a reservation.
///
/// DB / external-API boundary (no transaction around the HTTP call):
/// 1. The <see cref="Payment"/> — including its <c>IdempotencyKey</c> and the
///    exact <c>expiration_time</c> to send — is committed to PostgreSQL
///    FIRST.
/// 2. Only then is Mercado Pago called, with that persisted key.
/// 3. The response is saved in a separate, later commit.
/// If the process dies or the connection drops anywhere after step 1, the
/// row still exists; the user's retry finds it, sees it has no provider
/// order yet, and calls Mercado Pago again with the SAME key and the SAME
/// body, so the provider returns the already-created order instead of
/// creating a duplicate. A filtered unique index allows at most one
/// active (Created/Pending/Approved) payment per reservation, so two
/// concurrent requests cannot fork into two attempts either.
///
/// Amount and currency come exclusively from the reservation's historical
/// price snapshot via <see cref="IReservationPaymentContract"/>.
/// </summary>
public sealed class PaymentCreationService(
    AppDbContext dbContext,
    IMercadoPagoClient mercadoPagoClient,
    TimeProvider timeProvider)
{
    public async Task<InitiatedPayment> InitiateMercadoPagoAsync(
        PayableReservation reservation,
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

        var payment = await FindActivePaymentAsync(reservation.ReservationId, cancellationToken);

        if (payment is { Status: PaymentStatus.Approved })
        {
            throw new PaymentRequestException(
                "This reservation already has an approved payment.",
                StatusCodes.Status409Conflict);
        }

        payment ??= await CreatePaymentAsync(reservation, nowUtc, cancellationToken);

        if (payment.ProviderOrderId is null || payment.CheckoutUrl is null)
        {
            await CreateProviderOrderAsync(payment, cancellationToken);
        }

        return new InitiatedPayment(
            payment.Id,
            payment.ProviderOrderId!,
            payment.CheckoutUrl!,
            reservation.ExpiresAtUtc);
    }

    private Task<Payment?> FindActivePaymentAsync(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        dbContext.Payments.SingleOrDefaultAsync(
            candidate =>
                candidate.ReservationId == reservationId &&
                candidate.Method == PaymentMethod.MercadoPago &&
                (candidate.Status == PaymentStatus.Created ||
                 candidate.Status == PaymentStatus.Pending ||
                 candidate.Status == PaymentStatus.Approved),
            cancellationToken);

    private async Task<Payment> CreatePaymentAsync(
        PayableReservation reservation,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        // Derived from the remaining hold time; never a hardcoded duration.
        // The hold (Reservation.ExpiresAtUtc) stays the source of truth for
        // availability — the provider order simply stops accepting payment
        // at about the same moment. Whole seconds, at least one.
        var remaining = reservation.ExpiresAtUtc - nowUtc;
        var wholeSeconds = Math.Max(1, (long)Math.Floor(remaining.TotalSeconds));
        var expirationTime = XmlConvert.ToString(TimeSpan.FromSeconds(wholeSeconds));

        var payment = new Payment(
            Guid.NewGuid(),
            reservation.ReservationId,
            PaymentMethod.MercadoPago,
            reservation.TotalAmount,
            reservation.Currency!,
            Guid.NewGuid().ToString("D"),
            expirationTime,
            nowUtc);

        dbContext.Payments.Add(payment);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return payment;
        }
        catch (DbUpdateException error) when (UniqueViolation.IsUniqueViolation(error))
        {
            // A concurrent request created the single allowed active
            // attempt first: resume that one instead of forking.
            dbContext.ChangeTracker.Clear();

            return await FindActivePaymentAsync(reservation.ReservationId, cancellationToken)
                ?? throw new PaymentRequestException(
                    "Could not start a payment attempt; please retry.",
                    StatusCodes.Status409Conflict);
        }
    }

    private async Task CreateProviderOrderAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        MercadoPagoOrder order;

        try
        {
            order = await mercadoPagoClient.CreateOrderAsync(
                new CreateMercadoPagoOrderRequest(
                    payment.ExternalReference,
                    payment.Amount,
                    $"Amenity reservation {payment.ReservationId:N}",
                    payment.RequestedExpirationTime),
                payment.IdempotencyKey,
                cancellationToken);
        }
        catch (MercadoPagoUnavailableException)
        {
            // The Payment (and its idempotency key) is already committed and
            // stays Created; a retry re-sends the same key and body.
            throw new PaymentRequestException(
                "The payment provider is unavailable. Please try again.",
                StatusCodes.Status502BadGateway);
        }

        var nowUtc = timeProvider.GetUtcNow();

        payment.AttachProviderOrder(
            order.Id,
            order.CheckoutUrl ?? string.Empty,
            order.Status,
            order.StatusDetail,
            nowUtc);

        var mismatch = PaymentOrderValidator.Validate(payment, order)
            ?? (string.IsNullOrWhiteSpace(order.CheckoutUrl) ? "missing checkout url" : null);

        if (mismatch is not null)
        {
            payment.MarkRejected(nowUtc);
            await dbContext.SaveChangesAsync(cancellationToken);

            throw new PaymentRequestException(
                "The payment provider returned an order that does not match this payment.",
                StatusCodes.Status502BadGateway);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
