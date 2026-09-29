using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// "My reservations" (issue #66). Mirrors <see cref="ReservationAdminQuery"/>'s
/// efficient single-page projection (no aggregate loaded, no N+1), but the
/// building/membership filter is mandatory and non-optional: this query only
/// ever answers "reservations created by this one membership in this one
/// building" — never a general building listing.
/// </summary>
public sealed class ResidentReservationQuery(AppDbContext dbContext) : IResidentReservationQuery
{
    /// <remarks>
    /// Ordered by creation time, newest first (id as tiebreaker), matching
    /// <see cref="ReservationAdminQuery"/> so paging never skips or repeats a
    /// row when two reservations share the same CreatedAtUtc.
    /// </remarks>
    public async Task<ResidentReservationPage> ListAsync(
        Guid buildingId,
        Guid createdByMembershipId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, ReservationAdminQuery.MaxPageSize);

        var reservations = dbContext.Reservations
            .AsNoTracking()
            .Where(r =>
                r.BuildingId == buildingId &&
                r.CreatedByMembershipId == createdByMembershipId);

        var totalCount = await reservations.CountAsync(cancellationToken);

        var rows = await reservations
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(r => new
            {
                r.Id,
                r.BuildingId,
                r.UseType,
                r.Status,
                r.StartsAtUtc,
                r.EndsAtUtc,
                r.CreatedAtUtc,
                r.ExpiresAtUtc,
                r.ConfirmedAtUtc,
                r.CancelledAtUtc,
                r.ExpiredAtUtc,
                r.CancellationReason,
                Resources = r.Resources
                    .Select(resource => new { resource.AmenityId, resource.IsExclusive })
                    .ToList(),
                Total = r.PriceLines.Sum(line => (decimal?)line.Amount) ?? 0m,
                Currency = r.PriceLines.Select(line => line.Currency).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new ResidentReservationPage(
            rows.Select(r => new ResidentReservationRow(
                    r.Id,
                    r.BuildingId,
                    r.UseType.ToString(),
                    r.Status.ToString(),
                    r.StartsAtUtc,
                    r.EndsAtUtc,
                    r.CreatedAtUtc,
                    r.ExpiresAtUtc,
                    r.ConfirmedAtUtc,
                    r.CancelledAtUtc,
                    r.ExpiredAtUtc,
                    r.CancellationReason,
                    r.Resources
                        .Select(resource => new ResidentReservationResource(
                            resource.AmenityId,
                            resource.IsExclusive))
                        .ToList(),
                    r.Currency,
                    r.Total))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            totalCount);
    }
}
