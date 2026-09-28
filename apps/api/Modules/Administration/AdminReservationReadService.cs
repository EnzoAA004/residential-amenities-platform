using ResidentialAmenities.Api.Modules.Payments.Application;
using ResidentialAmenities.Api.Modules.Reservations.Application;

namespace ResidentialAmenities.Api.Modules.Administration;

public sealed record AdminReservationView(
    AdminReservationRow Reservation,
    IReadOnlyList<AdminPayment> Payments,
    bool RequiresFinancialReview);

public sealed record AdminReservationDetailView(
    AdminReservationRow Reservation,
    IReadOnlyList<AdminReservationPriceLine> PriceLines,
    IReadOnlyList<AdminPayment> Payments,
    bool RequiresFinancialReview);

public sealed record AdminReservationPageView(
    IReadOnlyList<AdminReservationView> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// Composes the administrative reservation read models. Administration owns no
/// data: reservation state comes from Reservations and payments from Payments,
/// each through its own query contract (two queries per page, no N+1).
///
/// <c>RequiresFinancialReview</c> is computed here and never stored: it is true
/// when money was taken for a reservation that is not (or is no longer) an
/// active booking — an approved payment on a cancelled reservation, or a
/// payment flagged for manual review. It does not alter the payment, and no
/// refund is implied (the cancellation/refund policy is open, OQ-011 / #2).
/// </summary>
public sealed class AdminReservationReadService(
    IReservationAdminQuery reservations,
    IPaymentAdminQuery payments)
{
    public async Task<AdminReservationPageView> ListAsync(
        AdminReservationFilter filter,
        CancellationToken cancellationToken)
    {
        var page = await reservations.ListAsync(filter, cancellationToken);

        var paymentsByReservation = await payments.GetByReservationsAsync(
            page.Items.Select(item => item.ReservationId).ToList(),
            cancellationToken);

        return new AdminReservationPageView(
            page.Items
                .Select(item =>
                {
                    var itemPayments = ForReservation(paymentsByReservation, item.ReservationId);
                    return new AdminReservationView(
                        item,
                        itemPayments,
                        RequiresFinancialReview(item, itemPayments));
                })
                .ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<AdminReservationDetailView?> GetAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var detail = await reservations.GetAsync(reservationId, cancellationToken);

        if (detail is null)
        {
            return null;
        }

        var paymentsByReservation = await payments.GetByReservationsAsync(
            [reservationId],
            cancellationToken);

        var reservationPayments = ForReservation(paymentsByReservation, reservationId);

        return new AdminReservationDetailView(
            detail.Reservation,
            detail.PriceLines,
            reservationPayments,
            RequiresFinancialReview(detail.Reservation, reservationPayments));
    }

    private static IReadOnlyList<AdminPayment> ForReservation(
        IReadOnlyDictionary<Guid, IReadOnlyList<AdminPayment>> byReservation,
        Guid reservationId) =>
        byReservation.TryGetValue(reservationId, out var found) ? found : [];

    private static bool RequiresFinancialReview(
        AdminReservationRow reservation,
        IReadOnlyList<AdminPayment> reservationPayments) =>
        reservationPayments.Any(payment => payment.RequiresManualReview) ||
        (reservation.Status == "Cancelled" &&
         reservationPayments.Any(payment => payment.Status == "Approved"));
}
