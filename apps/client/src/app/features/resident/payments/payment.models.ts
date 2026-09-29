/** Matches the backend's `Payment.Method` (`Modules/Payments/Domain/PaymentMethod.cs`). */
export type PaymentMethod = 'MercadoPago' | 'Cash';

/**
 * Matches the backend's `PaymentStatus` (`Modules/Payments/Domain/PaymentStatus.cs`) —
 * provider-neutral by design. `requiresManualReview` is a **separate**
 * boolean on {@link Payment}, never a status of its own: an `Approved`
 * payment can still require manual review (see
 * {@link PaymentReservationOutcome}).
 */
export type PaymentStatus = 'Created' | 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

/**
 * Matches the backend's `PaymentReservationOutcome`
 * (`Modules/Payments/Domain/PaymentReservationOutcome.cs`): what
 * Reservations decided when an approved payment was reported to it. Never
 * inferred from `PaymentStatus` — only the backend knows, e.g., whether an
 * `Approved` payment actually confirmed the reservation or arrived after it
 * expired/was cancelled.
 */
export type PaymentReservationOutcome =
  | 'None'
  | 'ReservationConfirmed'
  | 'ApprovedAfterExpiry'
  | 'ApprovedForCancelledReservation'
  | 'ApprovedForMissingReservation';

/**
 * Matches the backend's `GET /api/payments/{id}` response exactly
 * (`PaymentResponse` in `Modules/Payments/PaymentEndpoints.cs`) — the only
 * source of truth for a payment's real state. `cashConfirmedAtUtc` is the
 * only cash-confirmation field the resident-facing response exposes (no
 * `cashConfirmedByUserId` — that stays administrator-only).
 */
export interface Payment {
  paymentId: string;
  reservationId: string;
  method: PaymentMethod;
  status: PaymentStatus;
  amount: number;
  currency: string;
  approvedAtUtc: string | null;
  reservationOutcome: PaymentReservationOutcome;
  requiresManualReview: boolean;
  cashConfirmedAtUtc: string | null;
}

/**
 * Matches `POST /api/reservations/{id}/payments/mercadopago`'s response
 * (`InitiatePaymentResponse`). `providerOrderId` is modeled only because the
 * backend returns it — this client never displays, stores or forwards it.
 */
export interface InitiateMercadoPagoResponse {
  paymentId: string;
  providerOrderId: string;
  checkoutUrl: string;
  reservationExpiresAtUtc: string;
}

/** Matches `POST /api/reservations/{id}/payments/cash`'s response (`DeclareCashResponse`). */
export interface DeclareCashResponse {
  paymentId: string;
  status: 'Pending';
  reservationExpiresAtUtc: string;
}
