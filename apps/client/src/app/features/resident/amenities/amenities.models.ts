/**
 * Matches the backend's `AmenitySummaryResponse`
 * (`apps/api/Modules/Amenities/AmenityEndpoints.cs`) exactly. No extra
 * fields (price, capacity, image, description, next available slot, …):
 * the endpoint does not return them, so this client does not invent them.
 */
export interface AmenitySummary {
  id: string;
  name: string;
  kind: string;
  allowsSharedUse: boolean;
  allowsExclusiveUse: boolean;
}

/**
 * Matches the backend's `AvailabilityIntervalResponse`. Timestamps stay as
 * the ISO-8601 UTC strings the backend sends — never converted to a Date
 * (or anything else that loses the original value) before rendering.
 *
 * This is **structural** availability: recurring windows minus maintenance
 * periods, computed with no knowledge of existing reservations. It is not a
 * promise that a slot is still free — see `AmenityAvailabilityCalculator`.
 */
export interface AvailabilityInterval {
  startUtc: string;
  endUtc: string;
}
