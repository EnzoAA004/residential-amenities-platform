using ResidentialAmenities.Api.Modules.Amenities.Application;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Amenities;

public sealed class AmenityAvailabilityCalculatorTests
{
    private const string TimeZoneId = "America/Argentina/Buenos_Aires";
    private static readonly Guid AmenityId = Guid.NewGuid();

    [Fact]
    public void NoWindowsConfigured_ReturnsNoIntervals()
    {
        var result = AmenityAvailabilityCalculator.CalculateOpenIntervals(
            windows: [],
            unavailablePeriods: [],
            TimeZoneId,
            new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(result);
    }

    [Fact]
    public void SingleDayWindow_IsReturnedConvertedToUtc()
    {
        // 2026-10-05 is a Monday in the pilot time zone.
        var window = new AmenityAvailabilityWindow(
            Guid.NewGuid(),
            AmenityId,
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(22, 0));

        var result = AmenityAvailabilityCalculator.CalculateOpenIntervals(
            [window],
            [],
            TimeZoneId,
            new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));

        var interval = Assert.Single(result);

        var expectedStart = new DateTimeOffset(
            2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(-3));
        var expectedEnd = new DateTimeOffset(
            2026, 10, 5, 22, 0, 0, TimeSpan.FromHours(-3));

        Assert.Equal(expectedStart, interval.StartUtc);
        Assert.Equal(expectedEnd, interval.EndUtc);
    }

    [Fact]
    public void UnavailablePeriod_SplitsTheOpenWindow()
    {
        var window = new AmenityAvailabilityWindow(
            Guid.NewGuid(),
            AmenityId,
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(22, 0));

        var maintenanceStart = new DateTimeOffset(
            2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(-3));
        var maintenanceEnd = new DateTimeOffset(
            2026, 10, 5, 14, 0, 0, TimeSpan.FromHours(-3));

        var period = new AmenityUnavailablePeriod(
            Guid.NewGuid(),
            AmenityId,
            maintenanceStart,
            maintenanceEnd,
            "Maintenance");

        var result = AmenityAvailabilityCalculator.CalculateOpenIntervals(
            [window],
            [period],
            TimeZoneId,
            new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, result.Count);
        Assert.Equal(maintenanceStart, result[0].EndUtc);
        Assert.Equal(maintenanceEnd, result[1].StartUtc);
    }

    [Fact]
    public void UnavailablePeriod_CoveringEntireWindow_RemovesIt()
    {
        var window = new AmenityAvailabilityWindow(
            Guid.NewGuid(),
            AmenityId,
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(22, 0));

        var period = new AmenityUnavailablePeriod(
            Guid.NewGuid(),
            AmenityId,
            new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
            "Full closure");

        var result = AmenityAvailabilityCalculator.CalculateOpenIntervals(
            [window],
            [period],
            TimeZoneId,
            new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(result);
    }

    [Fact]
    public void QueryRangeExceedingMaximum_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AmenityAvailabilityCalculator.CalculateOpenIntervals(
                windows: [],
                unavailablePeriods: [],
                TimeZoneId,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void EndOfDaySentinel_ReachesExactMidnight_WithNoGap()
    {
        // DEC-014/OQ-002 (overnight Event slots): an amenity that must be
        // available right through midnight (e.g. to host a 20:00 -> 03:00
        // Event) is configured as two adjacent day windows —
        // Monday 20:00 -> "end of day" (EndOfDaySentinel, 23:59:59) and
        // Tuesday 00:00 -> 03:00. Taken literally (23:59:59 as a normal
        // time-of-day, or TimeOnly.MaxValue's 23:59:59.9999999 — which does
        // not survive round-tripping through a PostgreSQL `time` column's
        // microsecond precision), this pair would always fall short of full
        // coverage for any request spanning the boundary.
        var eveningWindow = new AmenityAvailabilityWindow(
            Guid.NewGuid(), AmenityId, DayOfWeek.Monday, new TimeOnly(20, 0),
            AmenityAvailabilityCalculator.EndOfDaySentinel);
        var morningWindow = new AmenityAvailabilityWindow(
            Guid.NewGuid(), AmenityId, DayOfWeek.Tuesday, new TimeOnly(0, 0), new TimeOnly(3, 0));

        // 2026-10-05 is a Monday, 2026-10-06 a Tuesday, in the pilot time zone.
        var requestStartUtc = new DateTimeOffset(2026, 10, 5, 20, 0, 0, TimeSpan.FromHours(-3));
        var requestEndUtc = new DateTimeOffset(2026, 10, 6, 3, 0, 0, TimeSpan.FromHours(-3));

        var result = AmenityAvailabilityCalculator.CalculateOpenIntervals(
            [eveningWindow, morningWindow],
            [],
            TimeZoneId,
            requestStartUtc,
            requestEndUtc);

        var coveredDuration = result.Aggregate(
            TimeSpan.Zero,
            (total, interval) => total + (interval.EndUtc - interval.StartUtc));

        Assert.Equal(requestEndUtc - requestStartUtc, coveredDuration);
    }

    [Fact]
    public void EndBeforeStart_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AmenityAvailabilityCalculator.CalculateOpenIntervals(
                windows: [],
                unavailablePeriods: [],
                TimeZoneId,
                new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero)));
    }
}
