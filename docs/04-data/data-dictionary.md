# Data Dictionary — v0.4

The model now includes the building/membership foundation, ASP.NET Core Identity
persistence, and the Amenities & Availability foundation (issue #19).

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
