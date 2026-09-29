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

/**
 * Response of the administrative `POST /payments/{paymentId}/cash/confirm`
 * (`ConfirmCashResponse` in `Modules/Payments/PaymentEndpoints.cs`). Reuses
 * the same resident-facing cash-confirm endpoint — there is no separate
 * `/admin/payments/.../confirm` route — but this record includes
 * `cashConfirmedByUserId`, which the resident-facing `GET /payments/{id}`
 * deliberately omits.
 */
export interface ConfirmCashPaymentResponse {
  paymentId: string;
  reservationId: string;
  status: AdminPaymentStatus | string;
  amount: number;
  currency: string;
  cashConfirmedAtUtc: string | null;
  cashConfirmedByUserId: string | null;
  reservationOutcome: string;
  requiresManualReview: boolean;
}

/**
 * Matches `Modules/Audit/Domain/AuditActorType.cs`. Not every audited event
 * has an authenticated user (a failed login, a background job, a Mercado
 * Pago webhook) — see `actorUserId` on {@link AuditItem}.
 */
export type AuditActorType = 'User' | 'System' | 'ExternalProvider';

/**
 * The stable audit action catalog, copied verbatim (including casing) from
 * `Modules/Audit/Domain/AuditAction.cs`. Never invent, rename or guess a
 * value here — this list only grows when the backend enum does.
 */
export const AUDIT_ACTIONS = [
  'AuthenticationSucceeded',
  'AuthenticationFailed',
  'Logout',
  'ReservationCreated',
  'ReservationConfirmed',
  'ReservationExpired',
  'ReservationCancelled',
  'ReservationRescheduled',
  'MercadoPagoPaymentInitiated',
  'PaymentApproved',
  'PaymentRejected',
  'PaymentCancelled',
  'CashPaymentDeclared',
  'CashPaymentConfirmed',
  'PaymentRequiresManualReview',
  'PriceRuleCreated',
  'PriceRuleSuperseded',
  'AmenityAvailabilityChanged',
  'EventSlotCreated',
  'EventSlotUpdated',
  'EventSlotDeactivated',
  'MercadoPagoWebhookProcessed'
] as const;

export type AuditAction = (typeof AUDIT_ACTIONS)[number];

/**
 * Copied verbatim from `Modules/Audit/Domain/AuditTargetType.cs`.
 */
export const AUDIT_TARGET_TYPES = [
  'Reservation',
  'Payment',
  'User',
  'PriceRule',
  'Amenity',
  'EventSlot'
] as const;

export type AuditTargetType = (typeof AUDIT_TARGET_TYPES)[number];

/**
 * Matches `AuditItemResponse` in `Modules/Audit/AuditEndpoints.cs` exactly.
 * `metadata` is rendered as-is (pretty-printed JSON) — it is never
 * reconstructed, enriched or cross-referenced with other data.
 */
export interface AuditItem {
  id: string;
  occurredAtUtc: string;
  buildingId: string | null;
  actorType: AuditActorType | string;
  actorUserId: string | null;
  action: AuditAction | string;
  targetType: AuditTargetType | string;
  targetId: string | null;
  correlationId: string | null;
  metadata: unknown | null;
}

export type AuditPage = AdminPage<AuditItem>;

/**
 * The exact filters `GET /admin/audit` accepts — no more, no less
 * (`Modules/Audit/AuditEndpoints.cs`). Backend clamps `pageSize` to
 * [1, 100] and `page` to >= 1 itself; the client never reimplements that.
 */
export interface AuditFilters {
  buildingId?: string;
  actorUserId?: string;
  action?: AuditAction;
  targetType?: AuditTargetType;
  targetId?: string;
  fromUtc?: string;
  toUtc?: string;
  page: number;
  pageSize: number;
}

// --- pricing --------------------------------------------------------------

export type AdminPriceComponentType = 'Base' | 'AddOn';

export interface AdminPriceRule {
  id: string;
  buildingId: string;
  amenityId: string;
  componentType: AdminPriceComponentType | string;
  useType: AdminReservationUseType | string;
  currency: string;
  amount: number;
  effectiveFromUtc: string;
  effectiveToUtc: string | null;
}

export type AdminPriceRulePage = AdminPage<AdminPriceRule>;

export interface AdminPriceRuleFilters {
  buildingId: string;
  amenityId?: string;
  activeAtUtc?: string;
  page: number;
  pageSize: number;
}

export interface CreatePriceRuleRequest {
  buildingId: string;
  amenityId: string;
  componentType: AdminPriceComponentType;
  useType: AdminReservationUseType;
  currency: string;
  amount: number;
  effectiveFromUtc?: string;
  effectiveToUtc?: string;
}

/**
 * A new rule is never an edit of the old one: it is effective-dated and any
 * prior rule for the same building/amenity/component/use type is superseded
 * (its `effectiveToUtc` is closed), never rewritten. The UI must always show
 * both `created` and `superseded` explicitly, and never describe this as
 * "editing" a rule.
 */
export interface PriceRuleCreationResult {
  created: AdminPriceRule;
  superseded: AdminPriceRule[];
}

// --- availability -----------------------------------------------------------

/**
 * Numeric `System.DayOfWeek` wire value: 0 = Sunday .. 6 = Saturday (matches
 * `Date.prototype.getDay()`), not a day name string — the backend has no
 * `JsonStringEnumConverter` registered, so enums serialize as their integer
 * value.
 */
export type AdminDayOfWeek = 0 | 1 | 2 | 3 | 4 | 5 | 6;

export interface AdminAvailabilityWindow {
  id: string;
  dayOfWeek: AdminDayOfWeek;
  /** `HH:mm:ss`. */
  startTime: string;
  /** `HH:mm:ss`. */
  endTime: string;
}

export interface AdminUnavailablePeriod {
  id: string;
  startsAtUtc: string;
  endsAtUtc: string;
  reason: string | null;
}

export interface AdminAvailabilityConfig {
  amenityId: string;
  buildingId: string;
  windows: AdminAvailabilityWindow[];
  unavailablePeriods: AdminUnavailablePeriod[];
}

export interface AvailabilityWindowInput {
  dayOfWeek: AdminDayOfWeek;
  /** `HH:mm:ss`. */
  startTime: string;
  /** `HH:mm:ss`. */
  endTime: string;
}

export interface ReplaceAvailabilityRequest {
  buildingId: string;
  windows: AvailabilityWindowInput[];
}

export interface CreateUnavailablePeriodRequest {
  buildingId: string;
  startsAtUtc: string;
  endsAtUtc: string;
  reason?: string | null;
}

export interface CreateUnavailablePeriodResult {
  periodId: string;
}

// --- event slots ------------------------------------------------------------

export interface AdminEventSlot {
  id: string;
  buildingId: string;
  name: string;
  /** `HH:mm:ss`. */
  startTime: string;
  /** `HH:mm:ss`. */
  endTime: string;
  isActive: boolean;
}

export interface EventSlotRequest {
  name: string;
  /** `HH:mm:ss`. */
  startTime: string;
  /** `HH:mm:ss`. */
  endTime: string;
}
