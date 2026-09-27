using ResidentialAmenities.Api.Modules.Amenities.Domain;

namespace ResidentialAmenities.Api.Modules.Amenities.Application;

/// <summary>
/// Computes structural availability (recurring operating windows minus
/// maintenance/unavailable periods) for a single amenity over a bounded
/// UTC range. This intentionally has no knowledge of concrete reservations;
/// per docs/03-architecture/module-boundaries.md, Reservations decides
/// whether a specific booking can be accepted after considering existing
/// bookings.
/// </summary>
public static class AmenityAvailabilityCalculator
{
    public static readonly TimeSpan MaxQueryRange = TimeSpan.FromDays(62);

    public static IReadOnlyList<AvailabilityInterval> CalculateOpenIntervals(
        IReadOnlyCollection<AmenityAvailabilityWindow> windows,
        IReadOnlyCollection<AmenityUnavailablePeriod> unavailablePeriods,
        string timeZoneId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        if (toUtc <= fromUtc)
        {
            throw new ArgumentException(
                "The range end must be after the range start.",
                nameof(toUtc));
        }

        if (toUtc - fromUtc > MaxQueryRange)
        {
            throw new ArgumentException(
                $"The queried range cannot exceed {MaxQueryRange.TotalDays} days.",
                nameof(toUtc));
        }

        if (windows.Count == 0)
        {
            return [];
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        var windowsByDay = windows
            .GroupBy(window => window.DayOfWeek)
            .ToDictionary(group => group.Key, group => group.ToList());

        var results = new List<AvailabilityInterval>();

        var localFrom = TimeZoneInfo.ConvertTime(fromUtc, timeZone).Date;
        var localToExclusive = TimeZoneInfo.ConvertTime(toUtc, timeZone).Date;

        for (var date = localFrom.AddDays(-1);
             date <= localToExclusive;
             date = date.AddDays(1))
        {
            if (!windowsByDay.TryGetValue(date.DayOfWeek, out var dayWindows))
            {
                continue;
            }

            foreach (var window in dayWindows)
            {
                var localStart = date.Add(window.StartTime.ToTimeSpan());
                var localEnd = date.Add(window.EndTime.ToTimeSpan());

                var windowStartUtc = ToUtc(localStart, timeZone);
                var windowEndUtc = ToUtc(localEnd, timeZone);

                var clippedStart =
                    windowStartUtc > fromUtc ? windowStartUtc : fromUtc;
                var clippedEnd = windowEndUtc < toUtc ? windowEndUtc : toUtc;

                if (clippedEnd <= clippedStart)
                {
                    continue;
                }

                results.AddRange(
                    SubtractUnavailablePeriods(
                        new AvailabilityInterval(clippedStart, clippedEnd),
                        unavailablePeriods));
            }
        }

        return results
            .OrderBy(interval => interval.StartUtc)
            .ToList();
    }

    private static DateTimeOffset ToUtc(DateTime localDateTime, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(
            localDateTime,
            DateTimeKind.Unspecified);

        return new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone),
            TimeSpan.Zero);
    }

    private static IEnumerable<AvailabilityInterval> SubtractUnavailablePeriods(
        AvailabilityInterval interval,
        IReadOnlyCollection<AmenityUnavailablePeriod> unavailablePeriods)
    {
        var remaining = new List<AvailabilityInterval> { interval };

        foreach (var period in unavailablePeriods)
        {
            if (period.EndsAtUtc <= interval.StartUtc ||
                period.StartsAtUtc >= interval.EndUtc)
            {
                continue;
            }

            var next = new List<AvailabilityInterval>();

            foreach (var candidate in remaining)
            {
                if (period.EndsAtUtc <= candidate.StartUtc ||
                    period.StartsAtUtc >= candidate.EndUtc)
                {
                    next.Add(candidate);
                    continue;
                }

                if (period.StartsAtUtc > candidate.StartUtc)
                {
                    next.Add(candidate with { EndUtc = period.StartsAtUtc });
                }

                if (period.EndsAtUtc < candidate.EndUtc)
                {
                    next.Add(candidate with { StartUtc = period.EndsAtUtc });
                }
            }

            remaining = next;
        }

        return remaining;
    }
}
