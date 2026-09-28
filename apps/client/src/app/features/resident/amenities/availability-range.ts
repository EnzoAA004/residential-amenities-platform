/**
 * Mirrors `AmenityAvailabilityCalculator.MaxQueryRange`
 * (`apps/api/Modules/Amenities/Application/AmenityAvailabilityCalculator.cs`).
 * This is UX validation only — the backend remains authoritative and
 * rejects an out-of-range query on its own.
 */
export const MAX_AVAILABILITY_RANGE_DAYS = 62;

export type AvailabilityRangeValidation =
  | { valid: true }
  | { valid: false; reason: string };

/**
 * Validates a browsing range before it is sent to the backend, so the UI
 * never fires a request the server would reject:
 * - both dates must be well-formed;
 * - the end must be after the start;
 * - the range must not exceed `MAX_AVAILABILITY_RANGE_DAYS`.
 */
export function validateAvailabilityRange(
  fromUtc: Date,
  toUtc: Date
): AvailabilityRangeValidation {
  if (Number.isNaN(fromUtc.getTime()) || Number.isNaN(toUtc.getTime())) {
    return { valid: false, reason: 'Enter valid start and end dates.' };
  }

  if (toUtc.getTime() <= fromUtc.getTime()) {
    return { valid: false, reason: 'The end date must be after the start date.' };
  }

  const maxRangeMs = MAX_AVAILABILITY_RANGE_DAYS * 24 * 60 * 60 * 1000;

  if (toUtc.getTime() - fromUtc.getTime() > maxRangeMs) {
    return {
      valid: false,
      reason: `The queried range cannot exceed ${MAX_AVAILABILITY_RANGE_DAYS} days.`
    };
  }

  return { valid: true };
}

/** A reasonable initial browsing window: today through the next 7 days. */
export function defaultAvailabilityRange(now: Date = new Date()): { fromUtc: Date; toUtc: Date } {
  const toUtc = new Date(now);
  toUtc.setDate(toUtc.getDate() + 7);
  return { fromUtc: now, toUtc };
}

/**
 * Formats a `Date` as the local wall-clock string an
 * `<input type="datetime-local">` expects (`YYYY-MM-DDTHH:mm`). The input
 * itself has no time zone concept — it always represents local time, and
 * `new Date(thatString)` parses it back as local time, so no custom
 * timezone conversion is needed on either side.
 */
export function toDateTimeLocalInputValue(date: Date): string {
  const pad = (value: number) => value.toString().padStart(2, '0');

  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}
