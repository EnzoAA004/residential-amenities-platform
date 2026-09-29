export type AdminReservationStatus = 'Pending' | 'Confirmed' | 'Cancelled' | 'Expired';
export type AdminReservationUseType = 'SharedLeisure' | 'ExclusiveLeisure' | 'Event';
export type AdminPaymentMethod = 'MercadoPago' | 'Cash';
export type AdminPaymentStatus = 'Created' | 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

export interface AdminPage<TItem> {
  items: TItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface AdminReservationResource {
  amenityId: string;
  isExclusive: boolean;
}

export interface AdminReservationRow {
  reservationId: string;
  buildingId: string;
  useType: AdminReservationUseType | string;
  status: AdminReservationStatus | string;
  startsAtUtc: string;
  endsAtUtc: string;
  createdAtUtc: string;
  confirmedAtUtc: string | null;
  cancelledAtUtc: string | null;
  expiredAtUtc: string | null;
  expiresAtUtc: string;
  cancellationReason: string | null;
  createdByMembershipId: string;
  resources: AdminReservationResource[];
  total: number;
  currency: string | null;
}

export interface AdminReservationPriceLine {
  amenityId: string;
  componentType: string;
  currency: string;
  amount: number;
}

export interface AdminPayment {
  paymentId: string;
  reservationId: string;
  buildingId: string;
  method: AdminPaymentMethod | string;
  status: AdminPaymentStatus | string;
  amount: number;
  currency: string;
  createdAtUtc: string;
  approvedAtUtc: string | null;
  reservationOutcome: string;
  requiresManualReview: boolean;
  providerStatus: string | null;
  providerStatusDetail: string | null;
  cashDeclaredAtUtc: string | null;
  cashConfirmedAtUtc: string | null;
  cashConfirmedByUserId: string | null;
}

export interface AdminReservationView {
  reservation: AdminReservationRow;
  payments: AdminPayment[];
  requiresFinancialReview: boolean;
}

export type AdminReservationPage = AdminPage<AdminReservationView>;
export type AdminPaymentPage = AdminPage<AdminPayment>;

export interface AdminReservationDetail {
  reservation: AdminReservationRow;
  priceLines: AdminReservationPriceLine[];
  payments: AdminPayment[];
  requiresFinancialReview: boolean;
}

export interface AdminReservationFilters {
  buildingId?: string;
  status?: AdminReservationStatus;
  useType?: AdminReservationUseType;
  fromUtc?: string;
  toUtc?: string;
  membershipId?: string;
  page: number;
  pageSize: number;
}

export interface AdminPaymentFilters {
  buildingId?: string;
  method?: AdminPaymentMethod;
  status?: AdminPaymentStatus;
  requiresManualReview?: boolean;
  reservationId?: string;
  fromUtc?: string;
  toUtc?: string;
  page: number;
  pageSize: number;
}

/**
 * Body for `POST /admin/reservations/{id}/cancel` (issue #52). `reason` is
 * the only field the client ever sends — the actor comes from the session
 * cookie, never from a client-supplied id/role.
 */
export interface AdminCancelReservationRequest {
  reason: string;
}

/**
 * Body for `POST /admin/reservations/{id}/reschedule` (issue #52).
 * `startsAtUtc`/`endsAtUtc` must be genuine UTC ISO-8601 instants built
 * without a browser-local `Date` parse — there is no `Building.TimeZoneId`
 * contract yet, so the UI never guesses an offset. `buildingId`, resources,
 * price/currency and `expiresAtUtc` are deliberately not part of this
 * request; the backend owns all of those.
 */
export interface AdminRescheduleReservationRequest {
  startsAtUtc: string;
  endsAtUtc: string;
  reason: string;
}
