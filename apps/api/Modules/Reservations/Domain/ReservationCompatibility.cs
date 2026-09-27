namespace ResidentialAmenities.Api.Modules.Reservations.Domain;

/// <summary>
/// Centralized overlap/compatibility rules (RB-003, RB-005, RF-010) so no
/// endpoint or service duplicates this logic. Evaluated per resource
/// (Amenity), never per building: reserving the SUM must not be treated as
/// blocking every amenity in the building.
///
/// This performs ordinary transactional conflict detection. It does not by
/// itself make two truly concurrent incompatible requests impossible — that
/// guarantee (RNF-005) is completed in issue #23 with real database-level
/// concurrency controls.
/// </summary>
public static class ReservationCompatibility
{
    /// <summary>
    /// Half-open interval overlap: [aStart, aEnd) intersects [bStart, bEnd).
    /// Contiguous ranges (a ends exactly when b starts) do not overlap.
    /// </summary>
    public static bool IntervalsOverlap(
        DateTimeOffset aStart,
        DateTimeOffset aEnd,
        DateTimeOffset bStart,
        DateTimeOffset bEnd) =>
        aStart < bEnd && bStart < aEnd;

    /// <summary>
    /// Shared + Shared is compatible (subject to a future configurable
    /// capacity — see docs/02-requirements/business-rules.md RB-016). Any
    /// combination touching an exclusive use is incompatible when the
    /// ranges overlap.
    /// </summary>
    public static bool ConflictsWith(
        bool existingIsExclusive,
        bool requestedIsExclusive,
        DateTimeOffset existingStart,
        DateTimeOffset existingEnd,
        DateTimeOffset requestedStart,
        DateTimeOffset requestedEnd)
    {
        if (!IntervalsOverlap(
                existingStart,
                existingEnd,
                requestedStart,
                requestedEnd))
        {
            return false;
        }

        return existingIsExclusive || requestedIsExclusive;
    }
}
