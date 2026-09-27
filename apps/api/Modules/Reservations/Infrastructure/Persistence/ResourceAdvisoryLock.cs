using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;

namespace ResidentialAmenities.Api.Modules.Reservations.Infrastructure.Persistence;

/// <summary>
/// Serializes reservation-creation conflict checks per Amenity using
/// PostgreSQL transaction-scoped advisory locks (RNF-005).
///
/// Why advisory locks instead of an exclusion constraint: the compatibility
/// rule (RB-003/RB-005) is asymmetric — any number of Shared bookings may
/// overlap each other, but an Exclusive booking must conflict with
/// everything overlapping it, shared or exclusive. A PostgreSQL EXCLUDE
/// constraint matches rows using an equality/overlap operator pair applied
/// uniformly to whichever rows satisfy its (optional) predicate; it cannot
/// express "this row conflicts with rows of a different kind" without
/// forcing every booking through the same equality key, which would
/// re-introduce a symmetric rule and break Shared+Shared coexistence. It
/// also cannot depend on other rows' current values at insert time, which
/// asymmetric compatibility requires per pair.
///
/// Serializable isolation was also considered and rejected here: it would
/// require every caller to implement a retry loop for serialization
/// failures, and its actual guarantees are easy to get subtly wrong without
/// dedicated testing this issue's time budget does not include.
///
/// Advisory locks instead give an explicit, provably correct serialization
/// point: a transaction acquires a lock per distinct AmenityId it is about
/// to book (in a fixed, sorted order — always by the Amenity's own Guid
/// bytes — so two transactions requesting overlapping resource sets in
/// different orders can never deadlock each other), then re-runs the
/// existing (already-correct) read-then-write conflict check. Because the
/// lock forces full serialization per Amenity, that check always sees any
/// concurrently-committed sibling transaction's writes, closing the race
/// that made a plain read-then-write check insufficient. Locks are
/// transaction-scoped (`pg_advisory_xact_lock`) so they are released
/// automatically on commit or rollback — no separate cleanup path exists to
/// forget.
/// </summary>
public static class ResourceAdvisoryLock
{
    public static async Task AcquireAsync(
        AppDbContext dbContext,
        IEnumerable<Guid> amenityIds,
        CancellationToken cancellationToken)
    {
        foreach (var amenityId in amenityIds.Distinct().OrderBy(id => id))
        {
            var (key1, key2) = ToLockKey(amenityId);

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({key1}, {key2})",
                cancellationToken);
        }
    }

    /// <summary>
    /// Folds a Guid's 16 bytes into two int32 lock keys. A hash collision
    /// between two different AmenityIds only causes unrelated bookings to
    /// briefly serialize against each other — never an incorrect result —
    /// which is the standard, accepted trade-off of advisory locks.
    /// </summary>
    private static (int Key1, int Key2) ToLockKey(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);

        var key1 = BitConverter.ToInt32(bytes[..4]);
        var key2 = BitConverter.ToInt32(bytes[4..8]) ^
                   BitConverter.ToInt32(bytes[8..12]) ^
                   BitConverter.ToInt32(bytes[12..16]);

        return (key1, key2);
    }
}
