/**
 * The only two reservation types this flow creates
 * (`apps/api/Modules/Pricing/Domain/ReservationUseType.cs` also defines
 * `Event`, which is out of scope here — issue #48). A closed union instead
 * of a free `string` so nothing but one of these two literal values can
 * ever reach the backend.
 */
export type LeisureUseType = 'SharedLeisure' | 'ExclusiveLeisure';

/** Matches the backend's `PriceQuoteLine` (`Modules/Pricing/Application/PriceQuoteLine.cs`). */
export interface PriceQuoteLine {
  priceRuleId: string;
  amenityId: string;
  componentType: string;
  currency: string;
  amount: number;
}

/** Matches the backend's `PriceQuote` (`Modules/Pricing/Application/PriceQuote.cs`). */
export interface PriceQuote {
  currency: string;
  totalAmount: number;
  quotedAtUtc: string;
  lines: PriceQuoteLine[];
}

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

/** Matches the backend's `ReservationResourceResponse`. */
export interface ReservationResource {
  amenityId: string;
  isExclusive: boolean;
}

/** Matches the backend's `ReservationPriceLineResponse`. */
export interface ReservationPriceLine {
  amenityId: string;
  componentType: string;
  currency: string;
  amount: number;
}

/**
 * Matches the backend's `ReservationResponse` exactly
 * (`Modules/Reservations/ReservationEndpoints.cs`) — this is the only
 * source of truth for the created hold's price/status/expiry; nothing here
 * is computed or assumed client-side.
 */
export interface Reservation {
  id: string;
  buildingId: string;
  useType: string;
  status: string;
  startsAtUtc: string;
  endsAtUtc: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  resources: ReservationResource[];
  priceLines: ReservationPriceLine[];
  currency: string;
  totalAmount: number;
}
