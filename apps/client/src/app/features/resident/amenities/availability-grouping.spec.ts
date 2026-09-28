import { describe, expect, it } from 'vitest';

import { groupIntervalsByLocalDay } from './availability-grouping';

describe('groupIntervalsByLocalDay', () => {
  it('returns an empty array for an empty response', () => {
    expect(groupIntervalsByLocalDay([])).toEqual([]);
  });

  it('groups intervals that fall on the same local day together, in order', () => {
    const groups = groupIntervalsByLocalDay([
      { startUtc: '2026-01-05T10:00:00Z', endUtc: '2026-01-05T13:00:00Z' },
      { startUtc: '2026-01-05T15:00:00Z', endUtc: '2026-01-05T20:00:00Z' },
      { startUtc: '2026-01-06T09:00:00Z', endUtc: '2026-01-06T12:00:00Z' }
    ]);

    expect(groups).toHaveLength(2);
    expect(groups[0].intervals).toHaveLength(2);
    expect(groups[1].intervals).toHaveLength(1);
  });

  it('preserves the backend-provided chronological order', () => {
    const groups = groupIntervalsByLocalDay([
      { startUtc: '2026-01-05T10:00:00Z', endUtc: '2026-01-05T13:00:00Z' },
      { startUtc: '2026-01-07T09:00:00Z', endUtc: '2026-01-07T12:00:00Z' }
    ]);

    expect(groups.map((group) => group.intervals[0].startLabel)).toBeDefined();
    expect(groups).toHaveLength(2);
  });

  it('does not lose or mutate the original interval count', () => {
    const intervals = [
      { startUtc: '2026-01-05T10:00:00Z', endUtc: '2026-01-05T13:00:00Z' },
      { startUtc: '2026-01-05T15:00:00Z', endUtc: '2026-01-05T20:00:00Z' }
    ];

    const groups = groupIntervalsByLocalDay(intervals);
    const totalIntervals = groups.reduce((sum, group) => sum + group.intervals.length, 0);

    expect(totalIntervals).toBe(intervals.length);
  });
});
