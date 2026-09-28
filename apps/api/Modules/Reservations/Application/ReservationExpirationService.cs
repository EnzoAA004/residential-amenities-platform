using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Releases expired payment holds (RF-012, RB-010): flips
/// <c>Pending</c> reservations whose <c>ExpiresAtUtc</c> has passed to
/// <c>Expired</c>, and audits each transition (RF-021).
///
/// It is still ONE atomic set-based statement — no per-row loop over loaded
/// entities: <c>UPDATE ... WHERE Status = 'Pending' AND ExpiresAtUtc &lt;= now
/// RETURNING ...</c>. PostgreSQL evaluates and locks each candidate row
/// individually and, under READ COMMITTED, a statement blocked on a row another
/// transaction is updating re-checks that row's <c>WHERE</c> once unblocked.
/// So with two overlapping runs, whichever commits first flips a row and the
/// other's predicate no longer matches it. <c>RETURNING</c> hands back exactly
/// the rows THIS statement transitioned, so each <c>Pending → Expired</c>
/// transition yields exactly one <c>ReservationExpired</c> audit entry — never
/// one for a reservation another run (or a concurrent confirmation) already
/// resolved.
///
/// The UPDATE and its audit entries are committed in the same transaction, so
/// a reservation is never expired without its audit fact (or the reverse). It
/// is idempotent, never touches Confirmed/Cancelled, and never revives an
/// Expired reservation.
/// </summary>
public sealed class ReservationExpirationService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IAuditRecorder auditRecorder)
{
    private const string ExpireSql = """
        UPDATE "Reservations"
        SET "Status" = 'Expired', "ExpiredAtUtc" = @now
        WHERE "Status" = 'Pending' AND "ExpiresAtUtc" <= @now
        RETURNING "Id", "BuildingId", "ExpiresAtUtc"
        """;

    public async Task<int> ExpirePastHoldsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().ToUniversalTime();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var expired = new List<(Guid Id, Guid BuildingId, DateTimeOffset ExpiresAtUtc)>();

        var command = dbContext.Database.GetDbConnection().CreateCommand();
        await using (command)
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = ExpireSql;
            command.Parameters.Add(new NpgsqlParameter("now", nowUtc));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                expired.Add((
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetFieldValue<DateTimeOffset>(2)));
            }
        }

        foreach (var (id, buildingId, expiresAtUtc) in expired)
        {
            auditRecorder.Record(AuditRecord.BySystem(
                AuditAction.ReservationExpired,
                AuditTargetType.Reservation,
                id,
                buildingId,
                AuditMetadata.ReservationExpired(expiresAtUtc)));
        }

        if (expired.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return expired.Count;
    }
}
