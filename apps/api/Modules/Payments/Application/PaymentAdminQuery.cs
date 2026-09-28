using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Payments.Domain;

namespace ResidentialAmenities.Api.Modules.Payments.Application;

public sealed record AdminPaymentFilter(
    Guid? BuildingId,
    PaymentMethod? Method,
    PaymentStatus? Status,
    bool? RequiresManualReview,
    Guid? ReservationId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Page,
    int PageSize);

/// <summary>
/// Administrative payment view. Deliberately excludes the idempotency key,
/// checkout URL, provider order id and any provider payload; it carries the
/// provider status only as the operational hint administrators need.
/// </summary>
public sealed record AdminPayment(
    Guid PaymentId,
    Guid ReservationId,
    Guid BuildingId,
    string Method,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    string ReservationOutcome,
    bool RequiresManualReview,
    string? ProviderStatus,
    string? ProviderStatusDetail,
    DateTimeOffset? CashDeclaredAtUtc,
    DateTimeOffset? CashConfirmedAtUtc,
    Guid? CashConfirmedByUserId);

public sealed record AdminPaymentPage(
    IReadOnlyList<AdminPayment> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>Read side for administrators. Read-only: no refund, resolve-review or edit exists.</summary>
public interface IPaymentAdminQuery
{
    Task<AdminPaymentPage> ListAsync(AdminPaymentFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<AdminPayment>>> GetByReservationsAsync(
        IReadOnlyCollection<Guid> reservationIds,
        CancellationToken cancellationToken);
}

public sealed class PaymentAdminQuery(AppDbContext dbContext) : IPaymentAdminQuery
{
    public const int MaxPageSize = 100;

    private static readonly PaymentReservationOutcome[] ManualReviewOutcomes =
    [
        PaymentReservationOutcome.ApprovedAfterExpiry,
        PaymentReservationOutcome.ApprovedForCancelledReservation,
        PaymentReservationOutcome.ApprovedForMissingReservation
    ];

    public async Task<AdminPaymentPage> ListAsync(
        AdminPaymentFilter filter,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var query = dbContext.Payments.AsNoTracking().AsQueryable();

        if (filter.BuildingId is { } buildingId)
        {
            query = query.Where(payment => payment.BuildingId == buildingId);
        }

        if (filter.Method is { } method)
        {
            query = query.Where(payment => payment.Method == method);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(payment => payment.Status == status);
        }

        if (filter.RequiresManualReview is { } review)
        {
            query = review
                ? query.Where(payment => ManualReviewOutcomes.Contains(payment.ReservationOutcome))
                : query.Where(payment => !ManualReviewOutcomes.Contains(payment.ReservationOutcome));
        }

        if (filter.ReservationId is { } reservationId)
        {
            query = query.Where(payment => payment.ReservationId == reservationId);
        }

        if (filter.FromUtc is { } fromUtc)
        {
            var from = fromUtc.ToUniversalTime();
            query = query.Where(payment => payment.CreatedAtUtc >= from);
        }

        if (filter.ToUtc is { } toUtc)
        {
            var to = toUtc.ToUniversalTime();
            query = query.Where(payment => payment.CreatedAtUtc < to);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await Project(query
                .OrderByDescending(payment => payment.CreatedAtUtc)
                .ThenByDescending(payment => payment.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(cancellationToken);

        return new AdminPaymentPage(rows.Select(ToView).ToList(), page, pageSize, totalCount);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AdminPayment>>> GetByReservationsAsync(
        IReadOnlyCollection<Guid> reservationIds,
        CancellationToken cancellationToken)
    {
        if (reservationIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<AdminPayment>>();
        }

        var rows = await Project(dbContext.Payments
                .AsNoTracking()
                .Where(payment => reservationIds.Contains(payment.ReservationId))
                .OrderByDescending(payment => payment.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return rows
            .Select(ToView)
            .GroupBy(payment => payment.ReservationId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AdminPayment>)group.ToList());
    }

    private static IQueryable<PaymentRow> Project(IQueryable<Payment> query) =>
        query.Select(payment => new PaymentRow(
            payment.Id,
            payment.ReservationId,
            payment.BuildingId,
            payment.Method,
            payment.Status,
            payment.Amount,
            payment.Currency,
            payment.CreatedAtUtc,
            payment.ApprovedAtUtc,
            payment.ReservationOutcome,
            payment.ProviderStatus,
            payment.ProviderStatusDetail,
            payment.CashDeclaredAtUtc,
            payment.CashConfirmedAtUtc,
            payment.CashConfirmedByUserId));

    private static AdminPayment ToView(PaymentRow row) =>
        new(
            row.Id,
            row.ReservationId,
            row.BuildingId,
            row.Method.ToString(),
            row.Status.ToString(),
            row.Amount,
            row.Currency,
            row.CreatedAtUtc,
            row.ApprovedAtUtc,
            row.ReservationOutcome.ToString(),
            ManualReviewOutcomes.Contains(row.ReservationOutcome),
            row.ProviderStatus,
            row.ProviderStatusDetail,
            row.CashDeclaredAtUtc,
            row.CashConfirmedAtUtc,
            row.CashConfirmedByUserId);

    private sealed record PaymentRow(
        Guid Id,
        Guid ReservationId,
        Guid BuildingId,
        PaymentMethod Method,
        PaymentStatus Status,
        decimal Amount,
        string Currency,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ApprovedAtUtc,
        PaymentReservationOutcome ReservationOutcome,
        string? ProviderStatus,
        string? ProviderStatusDetail,
        DateTimeOffset? CashDeclaredAtUtc,
        DateTimeOffset? CashConfirmedAtUtc,
        Guid? CashConfirmedByUserId);
}
