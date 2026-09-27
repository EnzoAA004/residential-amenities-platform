using ResidentialAmenities.Api.Modules.Reservations.Domain;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Reservations;

public sealed class ReservationCompatibilityTests
{
    private static readonly DateTimeOffset Ten =
        new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Eleven =
        new(2026, 10, 5, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Twelve =
        new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    // --- Interval overlap semantics -----------------------------------

    [Fact]
    public void FullOverlap_Overlaps()
    {
        Assert.True(ReservationCompatibility.IntervalsOverlap(
            Ten, Twelve, Ten, Twelve));
    }

    [Fact]
    public void PartialOverlapAtStart_Overlaps()
    {
        // existing 10-12, requested 09-11 -> overlaps 10-11
        var requestedStart = Ten.AddHours(-1);
        Assert.True(ReservationCompatibility.IntervalsOverlap(
            Ten, Twelve, requestedStart, Eleven));
    }

    [Fact]
    public void PartialOverlapAtEnd_Overlaps()
    {
        // existing 10-11, requested 10:30-12 -> overlaps 10:30-11
        var requestedStart = Ten.AddMinutes(30);
        Assert.True(ReservationCompatibility.IntervalsOverlap(
            Ten, Eleven, requestedStart, Twelve));
    }

    [Fact]
    public void ContainedRange_Overlaps()
    {
        // existing 10-12, requested 10:15-10:45 (contained)
        Assert.True(ReservationCompatibility.IntervalsOverlap(
            Ten,
            Twelve,
            Ten.AddMinutes(15),
            Ten.AddMinutes(45)));
    }

    [Fact]
    public void ContainerRange_Overlaps()
    {
        // existing 10:15-10:45, requested 10-12 (contains existing)
        Assert.True(ReservationCompatibility.IntervalsOverlap(
            Ten.AddMinutes(15),
            Ten.AddMinutes(45),
            Ten,
            Twelve));
    }

    [Fact]
    public void ContiguousRanges_DoNotOverlap()
    {
        // existing 10-11, requested 11-12
        Assert.False(ReservationCompatibility.IntervalsOverlap(
            Ten, Eleven, Eleven, Twelve));
    }

    [Fact]
    public void SeparateRanges_DoNotOverlap()
    {
        var farStart = Twelve.AddHours(1);
        var farEnd = Twelve.AddHours(2);
        Assert.False(ReservationCompatibility.IntervalsOverlap(
            Ten, Eleven, farStart, farEnd));
    }

    // --- Shared/Exclusive compatibility --------------------------------

    [Fact]
    public void SharedAndShared_Compatible()
    {
        Assert.False(ReservationCompatibility.ConflictsWith(
            existingIsExclusive: false,
            requestedIsExclusive: false,
            Ten, Eleven, Ten, Eleven));
    }

    [Fact]
    public void SharedThenExclusive_Incompatible()
    {
        Assert.True(ReservationCompatibility.ConflictsWith(
            existingIsExclusive: false,
            requestedIsExclusive: true,
            Ten, Eleven, Ten, Eleven));
    }

    [Fact]
    public void ExclusiveThenShared_Incompatible()
    {
        Assert.True(ReservationCompatibility.ConflictsWith(
            existingIsExclusive: true,
            requestedIsExclusive: false,
            Ten, Eleven, Ten, Eleven));
    }

    [Fact]
    public void ExclusiveThenExclusive_Incompatible()
    {
        Assert.True(ReservationCompatibility.ConflictsWith(
            existingIsExclusive: true,
            requestedIsExclusive: true,
            Ten, Eleven, Ten, Eleven));
    }

    [Fact]
    public void IncompatibleButNonOverlapping_NoConflict()
    {
        Assert.False(ReservationCompatibility.ConflictsWith(
            existingIsExclusive: true,
            requestedIsExclusive: true,
            Ten, Eleven, Eleven, Twelve));
    }
}
