/**
 * Matches the backend's resident-facing Event slot occurrence
 * (`EventSlotEndpoints.cs`, issue #62): a configured `EventSlotDefinition`
 * already expanded to a concrete UTC instant range for the requested
 * calendar date, using the building's own time zone server-side. This
 * client never recomputes `startsAtUtc`/`endsAtUtc` — it only forwards
 * them exactly as received.
 */
export interface EventSlotOccurrence {
  id: string;
  name: string;
  startsAtUtc: string;
  endsAtUtc: string;
  /** True when `endsAtUtc` falls on the calendar day after `startsAtUtc` (DEC-014/OQ-002). */
  isOvernight: boolean;
}

/**
 * Exactly the fields `POST /api/reservations` accepts for an Event
 * reservation (`CreateReservationRequest` in
 * `Modules/Reservations/ReservationEndpoints.cs`). `startsAtUtc`/`endsAtUtc`
 * must be copied verbatim from the selected `EventSlotOccurrence` — never
 * reconstructed from the chosen date. Deliberately excludes `membershipId`,
 * the slot's own `id`, and any price/status field — none of those are ever
 * sent from this client.
 */
export interface CreateEventReservationRequest {
  buildingId: string;
  amenityId: string;
  addOnAmenityIds: string[];
  useType: 'Event';
  startsAtUtc: string;
  endsAtUtc: string;
}
