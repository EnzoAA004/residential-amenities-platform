import { AvailabilityInterval } from './amenities.models';

export interface GroupedAvailabilityInterval {
  startLabel: string;
  endLabel: string;
}

export interface GroupedAvailabilityDay {
  dateLabel: string;
  intervals: GroupedAvailabilityInterval[];
}

/**
 * Groups the backend's raw open intervals by the device's local calendar
 * day, purely for display — this reads the timestamps, it never recomputes
 * or invents availability (RNF-004 stays a backend-only calculation).
 *
 * Timestamps are shown in the browser/device's local time zone via the
 * standard `Intl`/`Date` APIs, not the building's authoritative time zone
 * (the backend does not expose `Building.TimeZoneId` to this client yet —
 * see the client README for the documented gap).
 */
export function groupIntervalsByLocalDay(
  intervals: readonly AvailabilityInterval[]
): GroupedAvailabilityDay[] {
  const dayFormatter = new Intl.DateTimeFormat(undefined, {
    weekday: 'long',
    day: 'numeric',
    month: 'short'
  });
  const timeFormatter = new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit'
  });

  const groups = new Map<string, GroupedAvailabilityDay>();

  for (const interval of intervals) {
    const start = new Date(interval.startUtc);
    const end = new Date(interval.endUtc);
    const dayKey = start.toDateString();

    let group = groups.get(dayKey);

    if (!group) {
      group = { dateLabel: dayFormatter.format(start), intervals: [] };
      groups.set(dayKey, group);
    }

    group.intervals.push({
      startLabel: timeFormatter.format(start),
      endLabel: timeFormatter.format(end)
    });
  }

  return Array.from(groups.values());
}
