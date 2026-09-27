# Data Dictionary — v0.2

This dictionary describes the first persisted domain tables introduced by issue #9.

## Buildings

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. Stable building/tenant identifier. |
| Name | varchar(160) | No | Display name. Pilot value is development-only. |
| TimeZoneId | varchar(100) | No | IANA timezone used for building-local scheduling. |
| IsActive | boolean | No | Operational status. |

### Constraints

- Primary key: `Id`.

---

## Units

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| BuildingId | uuid | No | Owning building. |
| Floor | integer | No | Physical/display floor number. |
| Door | varchar(8) | No | Unit door/letter, normalized by domain creation. |
| Label | varchar(20) | No | Human-readable unit label such as `3A`. |
| IsActive | boolean | No | Operational status. |

### Constraints

- Primary key: `Id`.
- FK: `BuildingId -> Buildings.Id` with restrict delete.
- Unique: `(BuildingId, Label)`.
- Unique: `(BuildingId, Floor, Door)`.
- Alternate key: `(BuildingId, Id)`, used by the tenant-scoped membership FK.

---

## UserAccounts

`UserAccounts` is currently an identity/profile anchor, not the completed authentication implementation.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| Email | varchar(320) | No | Display/original normalized-trimmed email value. |
| NormalizedEmail | varchar(320) | No | Uppercase lookup value used for uniqueness. |
| DisplayName | varchar(160) | No | User-facing name. |
| IsActive | boolean | No | Account/profile operational state. |
| CreatedAtUtc | timestamptz | No | Creation timestamp. |

### Constraints

- Primary key: `Id`.
- Unique: `NormalizedEmail`.

### Deferred to #10

- credentials;
- password hashing/external identity;
- roles/policies;
- session/token lifecycle.

---

## ResidentMemberships

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | uuid | No | Primary key. |
| BuildingId | uuid | No | Explicit tenant/building context. |
| UnitId | uuid | No | Authorized unit. |
| UserId | uuid | No | User identity anchor. |
| Status | integer | No | `Pending=0`, `Active=1`, `Inactive=2`. |
| StartedAtUtc | timestamptz | No | Membership start. |
| EndedAtUtc | timestamptz | Yes | Membership end when deactivated. |

### Constraints

- Primary key: `Id`.
- Composite FK: `(BuildingId, UnitId) -> Units(BuildingId, Id)` with restrict delete.
- FK: `UserId -> UserAccounts.Id` with restrict delete.
- Unique: `(BuildingId, UnitId, UserId)`.
- Index: `UserId`.

## Why the composite foreign key matters

A standalone `UnitId` foreign key would prove only that the unit exists. The composite FK also proves that the supplied `BuildingId` and unit ownership agree.

That database-level invariant supports RNF-016 and ADR-009 by preserving a reliable building/tenant boundary from the first domain slice.
