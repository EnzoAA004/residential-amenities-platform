# Data Dictionary — v0.11

The model now includes the building/membership foundation, ASP.NET Core Identity
persistence, the Amenities & Availability foundation (issue #19), the Pricing
foundation (issue #22), Shared/Exclusive Leisure Reservations (issue #20),
Event Reservations + add-on amenities (issue #21), and reservation
concurrency/hold management (issue #23), Mercado Pago payments (issue #24), cash payments (issue #25), the audit trail (issue #27), and administrative operations (issue #26).

## Buildings

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. Stable building/tenant identifier. |
| Name | varchar(160) | No | Display name. Pilot value is development-only. |
| TimeZoneId | varchar(100) | No | IANA timezone used for building-local scheduling. |
| IsActive | boolean | No | Operational status. |

## Units

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| BuildingId | uuid | No | Owning building. |
| Floor | integer | No | Physical/display floor number. |
| Door | varchar(8) | No | Unit door/letter. |
| Label | varchar(20) | No | Human-readable label such as `3A`. |
| IsActive | boolean | No | Operational status. |

Constraints:

- FK `BuildingId -> Buildings.Id` with restrict delete.
- unique `(BuildingId, Label)`;
- unique `(BuildingId, Floor, Door)`;
- alternate key `(BuildingId, Id)`.

## UserAccounts

`UserAccount` extends ASP.NET Core `IdentityUser<Guid>`.

Application fields:

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| DisplayName | varchar(160) | No | User-facing name. |
| IsActive | boolean | No | Application account state. |
| CreatedAtUtc | timestamptz | No | Creation timestamp. |

Identity-managed fields include:

- `UserName`, `NormalizedUserName`;
- `Email`, `NormalizedEmail`, `EmailConfirmed`;
- `PasswordHash`;
- `SecurityStamp`, `ConcurrencyStamp`;
- `PhoneNumber`, `PhoneNumberConfirmed`;
- `TwoFactorEnabled`;
- `LockoutEnd`, `LockoutEnabled`, `AccessFailedCount`.

Important constraints:

- unique normalized username index;
- unique application index `UX_UserAccounts_NormalizedEmail`.

No plaintext password is stored.

## Identity supporting tables

ASP.NET Core Identity also owns:

| Table | Purpose |
| --- | --- |
| Roles | Application roles. Seeds `Resident` and `Administrator`. |
| UserRoles | Many-to-many user/role assignments. |
| UserClaims | User claims. |
| UserLogins | External-login records for future use if enabled. |
| UserTokens | Identity token records. |
| RoleClaims | Claims attached to roles. |

Role IDs are deterministic UUIDs so migrations/environments refer to the same two base roles.

## ResidentMemberships

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| BuildingId | uuid | No | Explicit tenant/building context. |
| UnitId | uuid | No | Authorized unit. |
| UserId | uuid | No | Identity user. |
| Status | integer | No | `Pending=0`, `Active=1`, `Inactive=2`. |
| StartedAtUtc | timestamptz | No | Membership start. |
| EndedAtUtc | timestamptz | Yes | Membership end when deactivated. |

Constraints:

- composite FK `(BuildingId, UnitId) -> Units(BuildingId, Id)` with restrict delete;
- FK `UserId -> UserAccounts.Id` with restrict delete;
- unique `(BuildingId, UnitId, UserId)`;
- index `UserId`.

## Amenities

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| BuildingId | uuid | No | Owning building. Never assume a single pilot building. |
| Name | varchar(160) | No | Display name (e.g. `SUM`, `Pool`, `Barbecue`). Configuration data, not a code constant. |
| Kind | varchar(30) | No | `Sum`, `Pool`, `Barbecue` or `Other`. Stored as string for readability/stability across enum reordering. |
| AllowsSharedUse | boolean | No | Whether the amenity supports shared/compatible concurrent use. |
| AllowsExclusiveUse | boolean | No | Whether the amenity supports exclusive booking. At least one of the two use flags must be true. |
| IsActive | boolean | No | Operational status. |

Constraints:

- unique `(BuildingId, Name)`;
- alternate key `(BuildingId, Id)`.

## AmenityAvailabilityWindows

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| AmenityId | uuid | No | Owning amenity. Cascade-deleted with it. |
| DayOfWeek | varchar(20) | No | Recurring weekly day. |
| StartTime | time | No | Local start of the operating window (building time zone). |
| EndTime | time | No | Local end of the operating window. Must be after `StartTime`; overnight windows are not supported yet. |

General amenity operating windows remain configuration. Event reservation
slot policy is separate and decided for the pilot in DEC-014/RB-019.
Development seed data uses a single 09:00-22:00 availability window per day
so the availability endpoint has something to query locally.

## AmenityUnavailablePeriods

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| AmenityId | uuid | No | Owning amenity. Cascade-deleted with it. |
| StartsAtUtc | timestamptz | No | Blackout start (maintenance, closure, etc.). |
| EndsAtUtc | timestamptz | No | Blackout end. Must be after `StartsAtUtc`. |
| Reason | varchar(280) | Yes | Optional free-text reason. |

## Availability query

`GET /api/amenities/{amenityId}/availability?fromUtc&toUtc` computes structural
availability (recurring windows minus unavailable periods) for a bounded range
(currently capped at 62 days). It does not consider concrete reservations —
per `docs/03-architecture/module-boundaries.md`, that decision belongs to the
Reservations module once it exists (issue #20/#21/#23).

## PriceRules

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| BuildingId | uuid | No | Owning building. |
| AmenityId | uuid | No | Priced amenity. Referenced by id only — no FK/navigation to `Amenities`, per the module-boundary rule against cross-module entity sharing. |
| ComponentType | varchar(20) | No | `Base` or `AddOn` (RB-002: pool/barbecue are independent optional add-ons). |
| UseType | varchar(30) | No | `SharedLeisure`, `ExclusiveLeisure` or `Event`. |
| Currency | varchar(3) | No | ISO currency code. Pilot uses `ARS`. |
| Amount | numeric(18,2) | No | Must be positive. |
| EffectiveFromUtc | timestamptz | No | Start of this rule's effective period. |
| EffectiveToUtc | timestamptz | Yes | End of the effective period, or open-ended when null. Set via `PriceRule.Supersede`, never by mutating `Amount` (RB-008). |

There is intentionally no unique constraint forcing exactly one active rule
per (BuildingId, AmenityId, ComponentType, UseType) at the database level yet
— `PricingCalculator` picks the most recently started effective rule and a
future admin workflow (#26) is expected to close the previous one via
`Supersede` before adding a new one.

## Pricing quote (not persisted)

`GET /api/pricing/quote` returns a `PriceQuote` { Currency, TotalAmount,
QuotedAtUtc, Lines[] }, where each `PriceQuoteLine` carries the exact
`PriceRuleId`, `AmenityId`, `ComponentType`, `Currency` and `Amount` that were
selected. This is the shape Reservations (#20/#21/#23) is expected to copy
into a future `ReservationPriceLine` table when a reservation is created —
the "historical snapshot" in RB-008 means copying these values, not keeping a
live reference to `PriceRules`.

## Reservations

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| BuildingId | uuid | No | Owning building. |
| CreatedByMembershipId | uuid | No | The `ResidentMembership.Id` that created it — resolved server-side from the authenticated caller, never a client-sent user/membership id. No FK (Reservations does not own Buildings' tables); kept as a plain historical reference. |
| UseType | varchar(30) | No | `SharedLeisure`, `ExclusiveLeisure` or `Event` (issue #21). Reuses Pricing's `ReservationUseType` enum directly (Reservations already depends on Pricing). |
| Status | varchar(20) | No | `Pending` (the payment hold, issue #23 — every new reservation starts here), `Confirmed`, `Cancelled` or `Expired`. See [Domain Model](domain-model.md#concurrency-and-holds-issue-23). |
| StartsAtUtc | timestamptz | No | Reservation range start. |
| EndsAtUtc | timestamptz | No | Reservation range end. Must be after `StartsAtUtc`. |
| CreatedAtUtc | timestamptz | No | Also the pricing quote timestamp for this reservation's price lines. |
| CancelledAtUtc | timestamptz | Yes | Set by `Reservation.Cancel`; no HTTP endpoint calls it yet. |
| ExpiresAtUtc | timestamptz | No | When this hold stops blocking resources if never confirmed (RB-009). Computed at creation as `now + Reservations:Hold:DurationMinutes` (configurable, RF-011). Checked live in conflict detection — a `Pending` row past this instant no longer blocks, even before the expiration job runs. |
| ExpiredAtUtc | timestamptz | Yes | Set by `ReservationExpirationService` when the hold is actually flipped to `Expired` (RB-010, RF-012). |
| ConfirmedAtUtc | timestamptz | Yes | Set by `Reservation.Confirm` (issue #24) when `Pending → Confirmed` happens through the Payments contract. Confirming again is a no-op and does not change it. |
| CancellationReason | varchar(500) | Yes | Why an administrator cancelled it (issue #26, RB-014). Set once by `Reservation.Cancel`; the audit trail also records it, but the reservation's current state does not depend on audit. |

Indexes: `(BuildingId, StartsAtUtc, EndsAtUtc)` for building/time-range
queries, `Status` for lifecycle filtering, `(Status, ExpiresAtUtc)` for the
expiration job's bulk `UPDATE ... WHERE`.

## ReservationResources

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| ReservationId | uuid | No | Owning reservation. Cascade-deleted with it. |
| AmenityId | uuid | No | Referenced by id only — no FK/navigation to `Amenities`. |
| IsExclusive | bool | No | Drives conflict detection for **this resource**, independent of any other amenity in the building. Leisure (#20) has exactly one resource per reservation; Event (#21) attaches the base SUM plus each selected add-on, every row `IsExclusive = true` (RB-006). |

Index: `(AmenityId, ReservationId)`, used by the conflict-detection query.

## ReservationPriceLines

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| ReservationId | uuid | No | Owning reservation. Cascade-deleted with it. |
| PriceRuleId | uuid | No | Historical reference only — **no FK** to `PriceRules`, so this row remains valid even if that rule is later superseded/removed (RB-008). |
| AmenityId | uuid | No | Historical reference only, same rationale. |
| ComponentType | varchar(20) | No | `Base` or `AddOn`, copied from the `PriceQuoteLine` at quote time. |
| Currency | varchar(3) | No | Copied at quote time. |
| Amount | numeric(18,2) | No | Copied at quote time; never recalculated. |
| QuotedAtUtc | timestamptz | No | When this snapshot was taken (equals the reservation's `CreatedAtUtc`). |

A reservation's total is `SUM(ReservationPriceLines.Amount)` for its id —
this never requires reading current `PriceRules`.

## Payments

Owned by the Payments module (issue #24). One row per payment **attempt**.
`ReservationId` is a plain reference — no FK, Payments does not own
Reservations' tables. See
[Domain Model](domain-model.md#payments-and-mercado-pago-issue-24).

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. Its `N` format is the `external_reference` sent to Mercado Pago and re-validated on every reconciliation. |
| ReservationId | uuid | No | Reservation being paid. |
| BuildingId | uuid | No | Building of the reservation, copied when the payment is created (issue #26) so Payments can scope admin queries without reading Reservations' tables. No FK. Backfilled from the reservation for older rows. |
| Method | varchar(30) | No | `MercadoPago` or `Cash` (issue #25). Same table, same model. |
| Status | varchar(20) | No | Provider-neutral: `Created`, `Pending`, `Approved`, `Rejected`, `Cancelled`. |
| Amount | numeric(18,2) | No | Copied from the `ReservationPriceLines` snapshot when the attempt is created; never from the client or current rules. |
| Currency | varchar(3) | No | Same source; validated against the provider's order. |
| IdempotencyKey | varchar(64) | Yes | **Mercado Pago only** (NULL for cash). Sent as `X-Idempotency-Key`; persisted **before** the HTTP call and reused by every retry of this attempt. Unique when present. |
| RequestedExpirationTime | varchar(40) | Yes | **Mercado Pago only** (NULL for cash). The exact ISO-8601 `expiration_time` sent, persisted so retries send an identical body. |
| ProviderOrderId | varchar(100) | Yes | Mercado Pago order id. Unique (NULLs are distinct, so many not-yet-created attempts coexist). |
| CheckoutUrl | varchar(2048) | Yes | Checkout Pro URL returned by the provider. |
| ProviderStatus / ProviderStatusDetail | varchar(100) | Yes | Provider's raw strings, kept apart from the neutral `Status`. |
| ReservationOutcome | varchar(40) | No | `None`, `ReservationConfirmed`, `ApprovedAfterExpiry`, `ApprovedForCancelledReservation`, `ApprovedForMissingReservation`. The last three mean money was taken but the reservation was not confirmed → manual review. |
| CreatedAtUtc / UpdatedAtUtc | timestamptz | No | |
| ApprovedAtUtc | timestamptz | Yes | Set once, when the provider verifiably credited it. |
| CashDeclaredAtUtc | timestamptz | Yes | **Cash only.** When the resident declared they would pay in cash. |
| CashConfirmedAtUtc | timestamptz | Yes | **Cash only.** When an authorized actor confirmed physical receipt. Set once; never overwritten. |
| CashConfirmedByUserId | uuid | Yes | **Cash only.** The authenticated `UserAccount.Id` who confirmed receipt. Historical id, no FK (module boundary). Set once. |

Provider-specific columns (`IdempotencyKey`, `RequestedExpirationTime`, `ProviderOrderId`, `CheckoutUrl`, `ProviderStatus`, `ProviderStatusDetail`) are NULL for cash and cash columns are NULL for Mercado Pago; the unique indexes ignore NULLs.

Indexes: unique `IdempotencyKey`; unique `ProviderOrderId`; **filtered unique
`UX_Payments_ActivePerReservation`** on `ReservationId WHERE Status IN
('Created','Pending','Approved')` (one live attempt per reservation);
non-unique `ReservationId`; non-unique `(BuildingId, CreatedAtUtc)` for the administrative listing.

No payer data, card data, tokens, signatures or raw provider payloads are
stored.

## PaymentProviderEvents

Idempotency ledger for webhook deliveries. Identifiers only.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| Provider | varchar(30) | No | e.g. `MercadoPago`. |
| ProviderEventId | varchar(200) | No | The signed `x-request-id` (fallback derived from order id/type/`ts`). |
| ProviderOrderId | varchar(100) | No | Order id from the signed `data.id` query parameter. |
| EventType | varchar(100) | No | The notification `action`, defaulting to `order`. |
| ReceivedAtUtc | timestamptz | No | |
| ProcessedAtUtc | timestamptz | Yes | Null while unprocessed (redelivery completes it). |
| ProcessingResult | varchar(30) | No | Outcome of reconciliation. |

Indexes: unique `(Provider, ProviderEventId)`; non-unique `ProviderOrderId`.

## AuditLogs

Owned by the Audit module (issue #27). Append-only history of business facts;
no foreign keys to any other table on purpose. See
[Domain Model](domain-model.md#audit-trail-issue-27).

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| OccurredAtUtc | timestamptz | No | When the fact was recorded (`TimeProvider`). |
| BuildingId | uuid | Yes | Building the target belongs to; NULL for global events such as a failed login. |
| ActorType | varchar(20) | No | `User`, `System` or `ExternalProvider`. |
| ActorUserId | uuid | Yes | `UserAccount.Id`; required for `User`, NULL otherwise. Historical id, no FK. |
| Action | varchar(60) | No | Member of the closed `AuditAction` catalog (stored by name). |
| TargetType | varchar(30) | No | `Reservation`, `Payment`, `User`, `PriceRule`, `Amenity`, `EventSlot`. |
| TargetId | uuid | Yes | Id of the target; NULL when there is none (e.g. failed login). |
| CorrelationId | varchar(100) | Yes | HTTP trace id for request-driven events; NULL for background jobs. |
| MetadataJson | jsonb | Yes | Small, allowlisted, server-built JSON. Never secrets, tokens, e-mails, bodies or payer data. |

Indexes: `(OccurredAtUtc)` for the default newest-first listing;
`(BuildingId, OccurredAtUtc)`; `(TargetType, TargetId, OccurredAtUtc)`;
`(ActorUserId, OccurredAtUtc)`; `(Action, OccurredAtUtc)` (per-action queries
such as `PaymentRequiresManualReview`). Rows are never updated or deleted by
the application; retention is not implemented yet.

## Concurrency: PostgreSQL advisory locks (issue #23)

No new table. Reservation creation runs inside an explicit transaction
(`dbContext.Database.BeginTransactionAsync`) that first calls
`pg_advisory_xact_lock(key1, key2)` once per distinct `AmenityId` in the
request (SUM + add-ons for Event), in ascending Guid order to avoid
deadlocks between two requests wanting overlapping resource sets. The two
`int` keys are folded from the Amenity's 16 Guid bytes
(`ResourceAdvisoryLock.ToLockKey`); a hash collision between two different
Amenities only costs an unnecessary bit of serialization, never an incorrect
result. Locks are transaction-scoped, so they release automatically on
commit or rollback. See
[Domain Model](domain-model.md#concurrency-and-holds-issue-23) for why this
was chosen over an exclusion constraint or `Serializable` isolation.

## EventSlotDefinitions

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| BuildingId | uuid | No | Owning building. |
| Name | varchar(80) | No | Display name (e.g. "afternoon", "evening"). Configuration data — see note below. |
| StartTime | time | No | Local start of the bookable Event window. |
| EndTime | time | No | Local end. Must be after `StartTime`; overnight/full-day slots are not supported yet. |
| IsActive | bool | No | Operational status. |

Constraint: unique `(BuildingId, StartTime, EndTime)`.

**Separate from `AmenityAvailabilityWindow`** (#19): that entity says when a
resource is physically usable at all; this says which windows within that
availability may be booked as an *Event* — a commercial/reservation policy
Amenities does not own. An Event's `startsAtUtc`/`endsAtUtc`, converted to
the building's local time zone, must match an active
`EventSlotDefinition.StartTime`/`EndTime` **exactly**, or the request is
rejected even if it falls inside the amenity's general availability.

DEC-014/RB-019 decides the pilot Event slots as afternoon 12:00-18:00 and
night 20:00-03:00 next day, building-local time. OQ-003 full-day remains
deferred.

## Separation of concerns

Identity role membership and residential membership are deliberately different:

```text
Identity role
  Resident / Administrator
        |
        | answers: what application privileges?
        v

ResidentMembership
  User + Building + Unit
        |
        | answers: where may this resident operate?
        v
```

This avoids treating an application role such as `Resident` as proof that a user belongs to a particular building or apartment.
