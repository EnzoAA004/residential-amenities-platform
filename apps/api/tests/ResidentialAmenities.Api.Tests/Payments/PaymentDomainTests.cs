using ResidentialAmenities.Api.Modules.Payments.Application;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Payments;

public sealed class PaymentDomainTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 1, 10, 0, 0, TimeSpan.Zero);

    // --- provider status -> internal status -------------------------------

    [Theory]
    [InlineData("processed", "accredited", ProviderOutcome.Approved)]
    [InlineData("PROCESSED", "ACCREDITED", ProviderOutcome.Approved)]
    [InlineData("created", "created", ProviderOutcome.Pending)]
    [InlineData("processing", "in_process", ProviderOutcome.Pending)]
    [InlineData("action_required", "waiting_payment", ProviderOutcome.Pending)]
    [InlineData("failed", "failed", ProviderOutcome.Rejected)]
    [InlineData("canceled", "canceled", ProviderOutcome.Cancelled)]
    [InlineData("expired", "expired", ProviderOutcome.Cancelled)]
    public void ProviderStatus_MapsToInternalOutcome(
        string status,
        string detail,
        ProviderOutcome expected)
    {
        Assert.Equal(expected, PaymentStatusMapper.Map(status, detail));
    }

    [Theory]
    [InlineData("processed", "partially_refunded")]
    [InlineData("processed", "")]
    [InlineData("refunded", "refunded")]
    [InlineData("charged_back", "settled")]
    [InlineData("something_new", "whatever")]
    public void OtherProviderStatuses_AreNotTreatedAsApproved(string status, string detail)
    {
        Assert.Equal(ProviderOutcome.Unmapped, PaymentStatusMapper.Map(status, detail));
    }

    // --- order vs payment validation --------------------------------------

    [Fact]
    public void MatchingOrder_Validates()
    {
        var payment = CreatePaymentWithOrder();

        Assert.Null(PaymentOrderValidator.Validate(payment, MatchingOrder(payment)));
    }

    [Fact]
    public void AmountMismatch_IsRejected()
    {
        var payment = CreatePaymentWithOrder();
        var order = MatchingOrder(payment) with { TotalAmount = payment.Amount - 1 };

        Assert.Equal("amount mismatch", PaymentOrderValidator.Validate(payment, order));
    }

    [Fact]
    public void CurrencyMismatch_IsRejected()
    {
        var payment = CreatePaymentWithOrder();
        var order = MatchingOrder(payment) with { Currency = "USD" };

        Assert.Equal("currency mismatch", PaymentOrderValidator.Validate(payment, order));
    }

    [Fact]
    public void ExternalReferenceMismatch_IsRejected()
    {
        var payment = CreatePaymentWithOrder();
        var order = MatchingOrder(payment) with { ExternalReference = "someone-elses" };

        Assert.Equal(
            "external_reference mismatch",
            PaymentOrderValidator.Validate(payment, order));
    }

    [Fact]
    public void OrderIdMismatch_IsRejected()
    {
        var payment = CreatePaymentWithOrder();
        var order = MatchingOrder(payment) with { Id = "OTHER" };

        Assert.Equal("order id mismatch", PaymentOrderValidator.Validate(payment, order));
    }

    // --- payment lifecycle -------------------------------------------------

    [Fact]
    public void NewPayment_HasOwnExternalReferenceAndPersistedIdempotencyKey()
    {
        var payment = CreatePayment();

        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Equal(payment.Id.ToString("N"), payment.ExternalReference);
        Assert.Equal("key-1", payment.IdempotencyKey);
        Assert.Equal("PT29M59S", payment.RequestedExpirationTime);
    }

    [Fact]
    public void AttachingProviderOrder_MovesToPending_AndMapsOrderFields()
    {
        var payment = CreatePayment();

        payment.AttachProviderOrder("ORD1", "https://checkout", "created", "created", Now);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("ORD1", payment.ProviderOrderId);
        Assert.Equal("https://checkout", payment.CheckoutUrl);
        Assert.Equal("created", payment.ProviderStatus);
    }

    [Fact]
    public void ApprovedPayment_IsFinal_AndApprovingTwiceIsANoOp()
    {
        var payment = CreatePayment();

        Assert.True(payment.MarkApproved(Now));
        Assert.False(payment.MarkApproved(Now.AddMinutes(5)));

        payment.MarkRejected(Now.AddMinutes(6));
        payment.MarkCancelled(Now.AddMinutes(7));
        payment.MarkPending(Now.AddMinutes(8));

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(Now, payment.ApprovedAtUtc);
    }

    [Fact]
    public void ApprovalAfterExpiry_IsExplicitlyMarkedForManualReview()
    {
        var payment = CreatePayment();
        payment.MarkApproved(Now);

        payment.RecordReservationOutcome(PaymentReservationOutcome.ApprovedAfterExpiry, Now);

        Assert.True(payment.RequiresManualReview);
    }

    [Fact]
    public void ConfirmedReservationOutcome_DoesNotRequireReview()
    {
        var payment = CreatePayment();
        payment.MarkApproved(Now);

        payment.RecordReservationOutcome(PaymentReservationOutcome.ReservationConfirmed, Now);

        Assert.False(payment.RequiresManualReview);
    }

    // --- Reservation.Confirm (the contract's domain transition) ------------

    [Fact]
    public void Reservation_PendingWithinHold_Confirms()
    {
        var reservation = CreateReservation();

        Assert.True(reservation.Confirm(Now.AddMinutes(10)));
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(Now.AddMinutes(10), reservation.ConfirmedAtUtc);
    }

    [Fact]
    public void Reservation_ConfirmingTwice_IsIdempotent()
    {
        var reservation = CreateReservation();
        var first = Now.AddMinutes(10);

        reservation.Confirm(first);
        Assert.True(reservation.Confirm(first.AddMinutes(1)));

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(first, reservation.ConfirmedAtUtc);
    }

    [Fact]
    public void Reservation_Expired_IsNeverRevived()
    {
        var reservation = CreateReservation();
        reservation.Expire(Now.AddHours(1));

        Assert.False(reservation.Confirm(Now.AddHours(2)));
        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Null(reservation.ConfirmedAtUtc);
    }

    [Fact]
    public void Reservation_PendingButPastDeadline_IsNotConfirmed()
    {
        // The job may not have run yet, but the hold no longer blocks
        // anyone, so its resources may already be someone else's.
        var reservation = CreateReservation();

        Assert.False(reservation.Confirm(Now.AddMinutes(31)));
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
    }

    [Fact]
    public void Reservation_Cancelled_IsNotConfirmed()
    {
        var reservation = CreateReservation();
        reservation.Cancel(Now.AddMinutes(1));

        Assert.False(reservation.Confirm(Now.AddMinutes(2)));
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
    }

    // --- cash payments (issue #25) ---------------------------------------------

    private static Payment CreateCashPayment() =>
        Payment.CreateCash(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5_000m, "ars", Now);

    [Fact]
    public void CashPayment_StartsPending_WithDeclarationTimestampAndNoConfirmation()
    {
        var payment = CreateCashPayment();

        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("ARS", payment.Currency);
        Assert.Equal(Now, payment.CashDeclaredAtUtc);
        Assert.Null(payment.CashConfirmedAtUtc);
        Assert.Null(payment.CashConfirmedByUserId);
        Assert.Null(payment.ApprovedAtUtc);
        Assert.Equal(PaymentReservationOutcome.None, payment.ReservationOutcome);
        Assert.False(payment.RequiresManualReview);
    }

    [Fact]
    public void CashPayment_HasNoProviderFieldsAndNoMercadoPagoIdempotency()
    {
        var payment = CreateCashPayment();

        Assert.Null(payment.IdempotencyKey);
        Assert.Null(payment.RequestedExpirationTime);
        Assert.Null(payment.ProviderOrderId);
        Assert.Null(payment.CheckoutUrl);
        Assert.Null(payment.ProviderStatus);
        Assert.Null(payment.ProviderStatusDetail);
    }

    [Fact]
    public void CashPayment_RejectsProviderOrderData()
    {
        var payment = CreateCashPayment();

        Assert.Throws<InvalidOperationException>(() =>
            payment.AttachProviderOrder("ORD", "https://x", "created", "created", Now));
        Assert.Null(payment.ProviderOrderId);
    }

    [Fact]
    public void ConfirmCashReceived_ApprovesAndRecordsActorAndTimestamp()
    {
        var payment = CreateCashPayment();
        var actor = Guid.NewGuid();

        Assert.True(payment.ConfirmCashReceived(actor, Now.AddHours(1)));

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(actor, payment.CashConfirmedByUserId);
        Assert.Equal(Now.AddHours(1), payment.CashConfirmedAtUtc);
        Assert.Equal(Now.AddHours(1), payment.ApprovedAtUtc);
    }

    [Fact]
    public void ConfirmCashReceived_Twice_IsIdempotentAndKeepsOriginalActorAndTimestamp()
    {
        var payment = CreateCashPayment();
        var firstActor = Guid.NewGuid();

        payment.ConfirmCashReceived(firstActor, Now.AddHours(1));

        Assert.False(payment.ConfirmCashReceived(Guid.NewGuid(), Now.AddHours(2)));

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(firstActor, payment.CashConfirmedByUserId);
        Assert.Equal(Now.AddHours(1), payment.CashConfirmedAtUtc);
        Assert.Equal(Now.AddHours(1), payment.ApprovedAtUtc);
    }

    [Fact]
    public void ConfirmCashReceived_OnNonCashPayment_IsRefused()
    {
        var payment = CreatePayment();

        Assert.Throws<InvalidOperationException>(() =>
            payment.ConfirmCashReceived(Guid.NewGuid(), Now));
        Assert.Equal(PaymentStatus.Created, payment.Status);
    }

    [Fact]
    public void ConfirmCashReceived_RequiresAnActor()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateCashPayment().ConfirmCashReceived(Guid.Empty, Now));
    }

    [Theory]
    [InlineData(PaymentReservationOutcome.ApprovedAfterExpiry, true)]
    [InlineData(PaymentReservationOutcome.ApprovedForCancelledReservation, true)]
    [InlineData(PaymentReservationOutcome.ReservationConfirmed, false)]
    public void LateCash_RequiresManualReviewOnlyWhenReservationCouldNotBeConfirmed(
        PaymentReservationOutcome outcome,
        bool expectedReview)
    {
        var payment = CreateCashPayment();
        payment.ConfirmCashReceived(Guid.NewGuid(), Now);
        payment.RecordReservationOutcome(outcome, Now);

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(expectedReview, payment.RequiresManualReview);
    }

    [Fact]
    public void MercadoPagoPayment_StillCarriesItsProviderFieldsAndNoCashFacts()
    {
        var payment = CreatePayment();

        Assert.Equal(PaymentMethod.MercadoPago, payment.Method);
        Assert.Equal("key-1", payment.IdempotencyKey);
        Assert.Equal("PT29M59S", payment.RequestedExpirationTime);
        Assert.Null(payment.CashDeclaredAtUtc);
        Assert.Null(payment.CashConfirmedAtUtc);
        Assert.Null(payment.CashConfirmedByUserId);
    }

    private static Payment CreatePayment() =>
        Payment.CreateMercadoPago(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            5_000m,
            "ARS",
            "key-1",
            "PT29M59S",
            Now);

    private static Payment CreatePaymentWithOrder()
    {
        var payment = CreatePayment();
        payment.AttachProviderOrder("ORD1", "https://checkout", "created", "created", Now);
        return payment;
    }

    private static MercadoPagoOrder MatchingOrder(Payment payment) =>
        new(
            payment.ProviderOrderId!,
            "created",
            "created",
            payment.ExternalReference,
            payment.Amount,
            payment.Currency,
            "https://checkout");

    private static Reservation CreateReservation() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReservationUseType.SharedLeisure,
            Now.AddDays(1),
            Now.AddDays(1).AddHours(1),
            Now,
            Now.AddMinutes(30));
}
