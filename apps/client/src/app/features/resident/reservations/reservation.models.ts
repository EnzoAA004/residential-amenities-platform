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
 * (`Modules/Reservations/ReservationEndpoints.cs`) for every `useType` —
 * this is the only source of truth for a created hold's
 * price/status/expiry; nothing here is computed or assumed client-side.
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
  confirmedAtUtc: string | null;
  cancelledAtUtc: string | null;
  expiredAtUtc: string | null;
  cancellationReason: string | null;
  resources: ReservationResource[];
  priceLines: ReservationPriceLine[];
  currency: string;
  totalAmount: number;
}
