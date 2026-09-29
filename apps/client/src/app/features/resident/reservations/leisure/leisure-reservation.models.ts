/**
 * The only two reservation types this flow creates
 * (`apps/api/Modules/Pricing/Domain/ReservationUseType.cs` also defines
 * `Event`, handled by the separate `reservations/event` feature — issue
 * #48). A closed union instead of a free `string` so nothing but one of
 * these two literal values can ever reach the backend.
 */
export type LeisureUseType = 'SharedLeisure' | 'ExclusiveLeisure';

/**
 * Exactly the fields `POST /api/reservations` accepts for a Leisure
 * reservation (`CreateReservationRequest` in
 * `Modules/Reservations/ReservationEndpoints.cs`). Deliberately excludes
 * `membershipId` (resolved server-side from the authenticated caller),
 * `addOnAmenityIds` (Event-only), and any price/status field — none of
 * those are ever sent from this client.
 */
export interface CreateLeisureReservationRequest {
  buildingId: string;
  amenityId: string;
  useType: LeisureUseType;
  startsAtUtc: string;
  endsAtUtc: string;
}
