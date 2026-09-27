# Data Dictionary — v0.6

The model now includes the building/membership foundation, ASP.NET Core Identity
persistence, the Amenities & Availability foundation (issue #19), the Pricing
foundation (issue #22), and Shared/Exclusive Leisure Reservations (issue #20).

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

Exact shift boundaries remain configuration, pending validation in issue #2
(`docs/01-discovery/assumptions-and-open-questions.md`). Development seed data
uses a single 09:00–22:00 placeholder window per day so the availability
endpoint has something to query locally.

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
| UseType | varchar(30) | No | `SharedLeisure`, `ExclusiveLeisure` or `Event`. Reuses Pricing's `ReservationUseType` enum directly (Reservations already depends on Pricing). This issue's endpoint only accepts the first two; `Event` is issue #21. |
| Status | varchar(20) | No | `Confirmed` or `Cancelled` only — see [Domain Model](domain-model.md#reservations--sharedexclusive-leisure-issue-20) for why payment-hold states are deferred. |
| StartsAtUtc | timestamptz | No | Reservation range start. |
| EndsAtUtc | timestamptz | No | Reservation range end. Must be after `StartsAtUtc`. |
| CreatedAtUtc | timestamptz | No | Also the pricing quote timestamp for this reservation's price lines. |
| CancelledAtUtc | timestamptz | Yes | Set by `Reservation.Cancel`; no HTTP endpoint calls it yet in this issue. |

Indexes: `(BuildingId, StartsAtUtc, EndsAtUtc)` for building/time-range
queries, `Status` for lifecycle filtering.

## ReservationResources

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| ReservationId | uuid | No | Owning reservation. Cascade-deleted with it. |
| AmenityId | uuid | No | Referenced by id only — no FK/navigation to `Amenities`. |
| IsExclusive | bool | No | Drives conflict detection for **this resource**, independent of any other amenity in the building. For this issue there is exactly one resource per reservation; issue #21 (Event) is expected to attach several. |

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
