using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

public sealed class ReservationAdminQuery(AppDbContext dbContext) : IReservationAdminQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <remarks>
    /// Ordered by creation time, newest first (id as tiebreaker) so a
    /// dashboard shows the latest activity; the time-range filters cover
    /// "what is coming up".
    /// </remarks>
    public async Task<AdminPage<AdminReservationRow>> ListAsync(
        AdminReservationFilter query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var reservations = dbContext.Reservations.AsNoTracking().AsQueryable();

        if (query.BuildingId is { } buildingId)
        {
            reservations = reservations.Where(r => r.BuildingId == buildingId);
        }

        if (query.Status is { } status)
        {
            reservations = reservations.Where(r => r.Status == status);
        }

        if (query.UseType is { } useType)
        {
            reservations = reservations.Where(r => r.UseType == useType);
        }

        if (query.MembershipId is { } membershipId)
        {
            reservations = reservations.Where(r => r.CreatedByMembershipId == membershipId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            var from = fromUtc.ToUniversalTime();
            reservations = reservations.Where(r => r.StartsAtUtc >= from);
        }

        if (query.ToUtc is { } toUtc)
        {
            var to = toUtc.ToUniversalTime();
            reservations = reservations.Where(r => r.StartsAtUtc < to);
        }

        var totalCount = await reservations.CountAsync(cancellationToken);

        // One page query with resources and totals projected in SQL (no N+1,
        // no aggregate loading).
        var rows = await reservations
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.BuildingId,
                r.UseType,
                r.Status,
                r.StartsAtUtc,
                r.EndsAtUtc,
                r.CreatedAtUtc,
                r.ConfirmedAtUtc,
                r.CancelledAtUtc,
                r.ExpiredAtUtc,
                r.ExpiresAtUtc,
                r.CancellationReason,
                r.CreatedByMembershipId,
                Resources = r.Resources
                    .Select(resource => new { resource.AmenityId, resource.IsExclusive })
                    .ToList(),
                Total = r.PriceLines.Sum(line => (decimal?)line.Amount) ?? 0m,
                Currency = r.PriceLines.Select(line => line.Currency).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new AdminPage<AdminReservationRow>(
            rows.Select(r => new AdminReservationRow(
                    r.Id,
                    r.BuildingId,
                    r.UseType.ToString(),
                    r.Status.ToString(),
                    r.StartsAtUtc,
                    r.EndsAtUtc,
                    r.CreatedAtUtc,
                    r.ConfirmedAtUtc,
                    r.CancelledAtUtc,
                    r.ExpiredAtUtc,
                    r.ExpiresAtUtc,
                    r.CancellationReason,
                    r.CreatedByMembershipId,
                    r.Resources
                        .Select(resource => new AdminReservationResource(
                            resource.AmenityId,
                            resource.IsExclusive))
                        .ToList(),
                    r.Total,
                    r.Currency))
                .ToList(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<AdminReservationDetail?> GetAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var page = await ListByIdAsync(reservationId, cancellationToken);

        if (page is null)
        {
            return null;
        }

        var lines = await dbContext.ReservationPriceLines
            .AsNoTracking()
            .Where(line => line.ReservationId == reservationId)
            .OrderBy(line => line.ComponentType)
            .Select(line => new AdminReservationPriceLine(
                line.AmenityId,
                line.ComponentType.ToString(),
                line.Currency,
                line.Amount))
            .ToListAsync(cancellationToken);

        return new AdminReservationDetail(page, lines);
    }

    private async Task<AdminReservationRow?> ListByIdAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Reservations
            .AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => new
            {
                r.Id,
                r.BuildingId,
                r.UseType,
                r.Status,
                r.StartsAtUtc,
                r.EndsAtUtc,
                r.CreatedAtUtc,
                r.ConfirmedAtUtc,
                r.CancelledAtUtc,
                r.ExpiredAtUtc,
                r.ExpiresAtUtc,
                r.CancellationReason,
                r.CreatedByMembershipId,
                Resources = r.Resources
                    .Select(resource => new { resource.AmenityId, resource.IsExclusive })
                    .ToList(),
                Total = r.PriceLines.Sum(line => (decimal?)line.Amount) ?? 0m,
                Currency = r.PriceLines.Select(line => line.Currency).FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new AdminReservationRow(
                row.Id,
                row.BuildingId,
                row.UseType.ToString(),
                row.Status.ToString(),
                row.StartsAtUtc,
                row.EndsAtUtc,
                row.CreatedAtUtc,
                row.ConfirmedAtUtc,
                row.CancelledAtUtc,
                row.ExpiredAtUtc,
                row.ExpiresAtUtc,
                row.CancellationReason,
                row.CreatedByMembershipId,
                row.Resources
                    .Select(resource => new AdminReservationResource(
                        resource.AmenityId,
                        resource.IsExclusive))
                    .ToList(),
                row.Total,
                row.Currency);
    }
}
