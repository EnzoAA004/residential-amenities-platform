export type ReservationEntryPointSuggestedUseType = 'SharedLeisure' | 'ExclusiveLeisure' | 'Event';

/**
 * Matches the backend's `ReservationEntryPointResponse`
 * (`apps/api/Modules/Reservations/ReservationEntryPointEndpoints.cs`).
 *
 * It is only safe context for starting an existing reservation flow. Pricing,
 * structural availability, event slots and final conflicts remain separate
 * backend authorities.
 */
export interface ReservationEntryPoint {
  token: string;
  buildingId: string;
  amenityId: string;
  displayName: string;
  amenityName: string;
  amenityKind: string;
  allowsSharedUse: boolean;
  allowsExclusiveUse: boolean;
  suggestedUseType: ReservationEntryPointSuggestedUseType | null;
}
