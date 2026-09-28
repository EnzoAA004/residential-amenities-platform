import { describe, expect, it } from 'vitest';

import {
  MAX_AVAILABILITY_RANGE_DAYS,
  defaultAvailabilityRange,
  toDateTimeLocalInputValue,
  validateAvailabilityRange
} from './availability-range';

describe('validateAvailabilityRange', () => {
  it('accepts a valid short range', () => {
    const from = new Date('2026-01-01T00:00:00Z');
    const to = new Date('2026-01-08T00:00:00Z');

    expect(validateAvailabilityRange(from, to)).toEqual({ valid: true });
  });

  it('rejects an invalid date', () => {
    const result = validateAvailabilityRange(new Date('not-a-date'), new Date('2026-01-08T00:00:00Z'));

    expect(result.valid).toBe(false);
  });

  it('rejects an end date that is not after the start date', () => {
    const same = new Date('2026-01-01T00:00:00Z');

    expect(validateAvailabilityRange(same, same).valid).toBe(false);
    expect(
      validateAvailabilityRange(new Date('2026-01-08T00:00:00Z'), new Date('2026-01-01T00:00:00Z')).valid
    ).toBe(false);
  });

  it(`rejects a range longer than ${MAX_AVAILABILITY_RANGE_DAYS} days`, () => {
    const from = new Date('2026-01-01T00:00:00Z');
    const to = new Date(from);
    to.setDate(to.getDate() + MAX_AVAILABILITY_RANGE_DAYS + 1);

    expect(validateAvailabilityRange(from, to).valid).toBe(false);
  });

  it(`accepts a range exactly ${MAX_AVAILABILITY_RANGE_DAYS} days long`, () => {
    const from = new Date('2026-01-01T00:00:00Z');
    const to = new Date(from);
    to.setDate(to.getDate() + MAX_AVAILABILITY_RANGE_DAYS);

    expect(validateAvailabilityRange(from, to).valid).toBe(true);
  });
});

describe('defaultAvailabilityRange', () => {
  it('spans the next 7 days from the given instant', () => {
    const now = new Date('2026-03-01T12:00:00Z');
    const { fromUtc, toUtc } = defaultAvailabilityRange(now);

    expect(fromUtc).toEqual(now);
    expect(toUtc.getTime() - fromUtc.getTime()).toBe(7 * 24 * 60 * 60 * 1000);
  });
});

describe('toDateTimeLocalInputValue', () => {
  it('formats a date as a local YYYY-MM-DDTHH:mm string and round-trips through Date', () => {
    const date = new Date(2026, 2, 5, 9, 7); // local: 2026-03-05 09:07
    const formatted = toDateTimeLocalInputValue(date);

    expect(formatted).toBe('2026-03-05T09:07');
    expect(new Date(formatted).getTime()).toBe(date.getTime());
  });

  it('zero-pads single-digit month, day, hour and minute', () => {
    const date = new Date(2026, 0, 2, 3, 4);

    expect(toDateTimeLocalInputValue(date)).toBe('2026-01-02T03:04');
  });
});
