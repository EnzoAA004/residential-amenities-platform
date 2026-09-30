# API

ASP.NET Core backend for Residential Amenities Platform.

## Runtime baseline

- .NET 10
- ASP.NET Core Minimal APIs
- ASP.NET Core Identity
- Entity Framework Core 10
- PostgreSQL 18 through Npgsql
- Built-in ASP.NET Core OpenAPI generation

## Solution

`ResidentialAmenities.slnx` contains the API and backend test project.

```bash
dotnet tool restore
dotnet restore apps/api/ResidentialAmenities.slnx
dotnet build apps/api/ResidentialAmenities.slnx
dotnet test apps/api/ResidentialAmenities.slnx
```

## Local PostgreSQL

From repository root:

```bash
docker compose up -d postgres
```

Set the API connection string outside source control.

Preferred local approach:

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=residential_amenities;Username=residential_app;Password=local_dev_only" --project apps/api/ResidentialAmenities.Api.csproj
```

A shell environment variable also works when useful for automation:

```powershell
$env:ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=residential_amenities;Username=residential_app;Password=local_dev_only"
```

The values above are development-only defaults. Real credentials must never be committed.

## EF Core migrations

Apply migrations:

```bash
dotnet tool restore
dotnet ef database update \
  --project apps/api/ResidentialAmenities.Api.csproj \
  --context AppDbContext
```

CI checks for pending model changes, so EF model changes without a matching migration fail validation.

## Development data

In Development, startup seeds:

- one pilot building;
- timezone `America/Argentina/Buenos_Aires`;
- units `1A` through `5B`.

Base Identity roles (`Resident` and `Administrator`) are seeded by the Identity migration.

No real resident/admin account is seeded by the application.

## Authentication

Public self-registration is intentionally disabled.

### Login

```http
POST /api/auth/login?useCookies=true
```

Use `useCookies=true` for the browser/web session.

For mobile/non-browser clients:

```http
POST /api/auth/login?useCookies=false
```

The response contains ASP.NET Core Identity bearer/refresh credentials. These are opaque application tokens, not custom JWTs.

### Refresh

```http
POST /api/auth/refresh
```

### Logout

```http
POST /api/auth/logout
```

Requires an authenticated session.

### Current user

```http
GET /api/auth/me
```

Returns the authenticated user's basic profile, roles and active residential memberships.

### RBAC verification endpoints

```http
GET /api/auth/check/resident
GET /api/auth/check/admin
```

These are foundation-phase endpoints used to verify Resident/Admin policies.

## Authentication defaults

- unique email;
- minimum password length 10 with upper/lower/digit/symbol;
- 5 failed attempts before 15-minute lockout;
- web cookie: HttpOnly, SameSite=Lax, sliding eight-hour lifetime;
- bearer access token: 20 minutes;
- bearer refresh token: 14 days.

Production HTTPS, client secure storage and account onboarding/recovery are refined in the security/onboarding work.

## Resident-facing endpoints

All require an authenticated Resident/Administrator session and an active
`ResidentMembership` for the building in question (Administrator bypasses
the membership check except when creating a reservation, which always
requires a real membership to attribute it to).

```http
GET  /api/buildings/{buildingId}/amenities
GET  /api/amenities/{amenityId}/availability?fromUtc=&toUtc=
GET  /api/buildings/{buildingId}/event-slots?date=YYYY-MM-DD
GET  /api/pricing/quote?buildingId=&amenityId=&useType=&addOnAmenityId=&atUtc=
GET  /api/reservation-entry-points/{token}
POST /api/reservations
GET  /api/reservations?buildingId=&page=&pageSize=
GET  /api/reservations/{id}
GET  /api/reservations/{reservationId}/payments
GET  /api/notification-subscriptions
POST /api/notification-subscriptions
DELETE /api/notification-subscriptions/{id}
```

### Notification subscriptions (issue #77)

`GET/POST/DELETE /api/notification-subscriptions` is the push-notification
foundation. The endpoints require `ResidentAccess` and always derive `UserId`
from the authenticated principal; request bodies never accept a user id,
membership id or building id.

`POST` currently accepts the minimal subscription material needed for a future
sender:

```json
{
  "platform": "WebPush",
  "endpoint": "https://push.example/subscription/...",
  "p256Dh": "...",
  "auth": "...",
  "userAgent": "optional client user agent"
}
```

The endpoint validates an HTTPS endpoint and required keys, upserts by
`UserId + endpointHash`, and returns safe metadata only:

```json
{
  "id": "...",
  "platform": "WebPush",
  "endpointHash": "sha256...",
  "isEnabled": true,
  "createdAtUtc": "2026-09-30T00:00:00Z",
  "updatedAtUtc": "2026-09-30T00:00:00Z"
}
```

Raw endpoints and secrets are stored for a future sender but are never echoed
from read endpoints. `DELETE` unregisters only a subscription owned by the
authenticated user. This phase deliberately does not send notifications,
choose cloud infrastructure, or define reservation/payment reminder rules.

### Reservation entry points (issue #80)

`GET /api/reservation-entry-points/{token}` resolves an opaque QR/link token
into the safe reservation context the client needs to start a resident booking
flow: `buildingId`, `amenityId`, display labels, amenity kind, allowed use
modes and an optional `suggestedUseType`.

The token is not a direct database id and is normalized case-insensitively by
the API. The endpoint requires `ResidentAccess` and validates the caller's
active membership against the resolved building before returning data:
unauthenticated requests return `401`, valid tokens for buildings the caller
cannot access return `403`, and unknown, inactive or no-longer-valid entry
points return `404`.

This endpoint intentionally does not calculate pricing, payment state or final
availability. Clients must still call `GET /api/pricing/quote`, availability
and/or event-slot endpoints as needed, and `POST /api/reservations` remains the
authority for conflicts and reservation business validation.

### Resident Event slot discovery (issue #62)

`GET /api/buildings/{buildingId}/event-slots?date=YYYY-MM-DD` — distinct
from the Administrator-only `GET /api/admin/buildings/{buildingId}/event-slots`
below (which also lists inactive slots and admin fields). This one requires
`ResidentAccess` + `IBuildingMembershipAuthorizer` like the other
resident-facing endpoints above; it never reuses the admin endpoint or
relaxes its policy.

It returns only the building's **active** `EventSlotDefinition`s, expanded
to UTC instants for the requested calendar date using `Building.TimeZoneId`
— the client never combines a date with `EventSlotDefinition.StartTime`
using its own device time zone, which would silently miscompute
`startsAtUtc`/`endsAtUtc` for any building not in the server/device's zone:

```json
[
  { "id": "...", "name": "Afternoon", "startsAtUtc": "2026-10-01T17:00:00Z", "endsAtUtc": "2026-10-01T22:00:00Z" }
]
```

If the requested date/time combination is invalid (spring-forward DST gap)
or ambiguous (fall-back DST repeat) in the building's time zone, the
endpoint returns `422` rather than silently picking one of two possible UTC
offsets.

This is **configured** Event slots, not final availability: it never reads
`Reservations`/`ReservationResources`/pricing/amenity availability to decide
what to return, the same way `GET /api/amenities/{id}/availability` reports
structural availability without knowing about reservations. A slot that is
already fully booked (or in maintenance) still appears here; `POST
/api/reservations` remains the sole authority for conflicts and business
validation (`409`/`422`) when a resident actually tries to book one.

`POST /api/reservations` supports `useType` values `SharedLeisure`,
`ExclusiveLeisure` and `Event`. Shared/Exclusive Leisure request body:

```json
{
  "buildingId": "...",
  "amenityId": "...",
  "useType": "SharedLeisure",
  "startsAtUtc": "2027-03-02T13:00:00Z",
  "endsAtUtc": "2027-03-02T14:00:00Z"
}
```

Event reservations may include optional `addOnAmenityIds`; `amenityId` is
the base resource, which must be an active amenity of kind `Sum`. Each
add-on must be a distinct, active amenity from the same building, of kind
`Pool` or `Barbecue`, and must allow exclusive use — the backend does not
impose a separate numeric maximum on the number of add-ons. The requested
range must match a configured `EventSlotDefinition` exactly — an arbitrary
range that merely falls inside the SUM's general availability is rejected:

```json
{
  "buildingId": "...",
  "amenityId": "...",
  "addOnAmenityIds": ["...", "..."],
  "useType": "Event",
  "startsAtUtc": "2027-03-02T17:00:00Z",
  "endsAtUtc": "2027-03-02T22:00:00Z"
}
```

The membership, price and availability are always derived/validated
server-side — a client cannot influence them by sending extra fields.

### Holds and concurrency (issue #23)

Every reservation is created as a `Pending` **hold** (RB-009) — there is no
payment yet at creation time, so `Pending` is the only state a fresh
reservation can start in (it becomes `Confirmed` through a verified payment,
see below). The response's `status` field will read `"Pending"`, not
`"Confirmed"`, and includes `expiresAtUtc`. A hold blocks its resources
exactly like a `Confirmed` reservation until that instant, then stops
blocking automatically — a background service releases past-due holds
(flips them to `Expired`) every
`Reservations:Expiration:IntervalSeconds` seconds (default 60), and the
conflict-detection query itself already treats an expired-but-not-yet-swept
hold as inactive.

Hold duration is configurable, **not** hardcoded:

```json
{
  "Reservations": {
    "Hold": { "DurationMinutes": 30 },
    "Expiration": { "IntervalSeconds": 60 }
  }
}
```

30 minutes is a placeholder for local development — production must set
this once issue #2 answers OQ-010 (candidates under discussion: 24 or 48
hours).

Two truly concurrent, incompatible requests for the same resource/time can
never both succeed: reservation creation acquires a PostgreSQL
transaction-scoped advisory lock per requested Amenity before checking for
conflicts, serializing concurrent attempts on the same resource. See
`docs/04-data/domain-model.md#concurrency-and-holds-issue-23` for why this
approach was chosen over a database exclusion constraint or `Serializable`
isolation.

### Resident reservation history and payment reads (issue #66)

```http
GET /api/reservations?buildingId=&page=&pageSize=       ("my reservations")
GET /api/reservations/{id}                                 (owner, or Administrator)
GET /api/reservations/{reservationId}/payments              (owner, or Administrator)
```

**"My reservations"** is `buildingId` (required) filtered to reservations
`CreatedByMembershipId` matches the caller's own **active** membership in
that building, resolved server-side by `IBuildingMembershipAuthorizer`
exactly like reservation creation. The client can never make this list show
someone else's reservations: it does not accept `membershipId`, `userId` or
`createdByMembershipId` as query parameters, and passing them is silently
ignored. No active membership in `buildingId` → `403`, not an empty page (so
"forbidden" and "no reservations yet" are never confused). Paginated like
the administrative read models: `page` defaults to `1`, `pageSize` defaults
to `50` and is clamped to `100`; results are ordered `CreatedAtUtc DESC`,
`Id DESC` (a tiebreaker, so paging is stable even when two reservations
share a timestamp). Each item is a summary (no per-line pricing, no
`CreatedByMembershipId` or other actor id — the caller already knows these
are their own):

```json
{
  "items": [{
    "id": "...", "buildingId": "...",
    "useType": "SharedLeisure", "status": "Confirmed",
    "startsAtUtc": "...", "endsAtUtc": "...",
    "createdAtUtc": "...", "expiresAtUtc": "...",
    "confirmedAtUtc": "...", "cancelledAtUtc": null, "expiredAtUtc": null,
    "cancellationReason": null,
    "resources": [{ "amenityId": "...", "isExclusive": false }],
    "currency": "ARS", "totalAmount": 5000
  }],
  "page": 1, "pageSize": 50, "totalCount": 1
}
```

`GET /api/reservations/{id}`'s object-level authorization was hardened: it
previously only checked building-level access (`HasAccessAsync`), so any
resident with an active membership in the same building could read another
resident's reservation just by knowing/guessing its id. It now also requires
`CreatedByMembershipId` to match the caller's own active membership for a
non-Administrator caller; a same-building resident who did not create the
reservation gets `403`, same as a missing membership. Administrator keeps
the existing full-access bypass. The response gained (additive, nothing
renamed/removed) `confirmedAtUtc`, `cancelledAtUtc`, `expiredAtUtc` and
`cancellationReason` — always copied straight from `Reservation`, never
inferred from the clock or from a payment.

`GET /api/reservations/{reservationId}/payments` is the resident-facing
payment **history** — plural and always a list (`200` with `[]` when there
are none, never `404` for "no payment yet"), because a reservation can have
more than one payment attempt: a `Rejected`/`Cancelled` attempt does not
block a new one while the hold is still valid. Ordered `CreatedAtUtc DESC`,
`Id DESC`, newest first. Ownership is enforced the same way as the detail
endpoint above (Administrator bypass; otherwise the caller's active
membership must match the reservation's `CreatedByMembershipId`), resolved
through `IReservationPaymentContract.GetPayableReservationAsync` — Payments
still never reads a Reservations table directly (module boundary). Each
item is the same explicit resident DTO `GET /api/payments/{id}` already
returns (plus `createdAtUtc`), and equally never exposes
`idempotencyKey`/`checkoutUrl`/`providerOrderId`/`providerStatus*`/
`cashConfirmedByUserId`:

```json
[
  {
    "paymentId": "...", "reservationId": "...",
    "method": "Cash", "status": "Pending",
    "amount": 5000, "currency": "ARS",
    "createdAtUtc": "...", "approvedAtUtc": null,
    "reservationOutcome": "None", "requiresManualReview": false,
    "cashConfirmedAtUtc": null
  }
]
```

### Payments — Mercado Pago (issue #24)

```http
POST /api/reservations/{reservationId}/payments/mercadopago   (resident, building member)
GET  /api/payments/{paymentId}                                  (resident, building member)
POST /api/webhooks/mercadopago                                  (public; x-signature verified)
```

`POST .../payments/mercadopago` creates a Checkout Pro order through the
Mercado Pago **Orders API** and returns `{ paymentId, providerOrderId,
checkoutUrl, reservationExpiresAtUtc }`. The client sends **no** amount or
currency: both come from the reservation's price snapshot. The reservation
must still be `Pending` and inside its hold. A retry while an attempt is live
returns the same attempt (same idempotency key); after a rejected/cancelled
attempt a new one can be started while the hold lasts.

The reservation only becomes `Confirmed` when Mercado Pago's webhook is
verified **and** the order re-fetched from Mercado Pago is `processed` +
`accredited`. The browser return URLs never confirm anything; the client
should poll `GET /api/payments/{paymentId}`. A payment approved after the
hold expired leaves the reservation `Expired` and is reported with
`reservationOutcome: "ApprovedAfterExpiry"` and `requiresManualReview: true`.

Configuration names (values are **never** committed; use User Secrets or
environment variables):

```text
MercadoPago:AccessToken          (secret)
MercadoPago:WebhookSecret        (secret)
MercadoPago:ApiBaseUrl           (default https://api.mercadopago.com)
MercadoPago:SuccessUrl / PendingUrl / FailureUrl
MercadoPago:WebhookToleranceSeconds   (default 600)
MercadoPago:RequestTimeoutSeconds     (default 15)
```

```bash
dotnet user-secrets set "MercadoPago:AccessToken" "<test access token>" --project apps/api/ResidentialAmenities.Api.csproj
dotnet user-secrets set "MercadoPago:WebhookSecret" "<webhook secret>" --project apps/api/ResidentialAmenities.Api.csproj
```

### Payments — cash (issue #25)

```http
POST /api/reservations/{reservationId}/payments/cash   (reservation creator, or Administrator)
POST /api/payments/{paymentId}/cash/confirm            (Administrator only)
```

`POST .../payments/cash` means "I want to pay this in cash", **not** "cash
received". It returns `{ paymentId, status: "Pending", reservationExpiresAtUtc }`.
The reservation stays `Pending` and its hold is not extended. Calling it again
returns the same payment. If a Mercado Pago payment is already active for the
reservation (or the reverse) the answer is `409`; switching method is not
supported. The body is empty — amount and currency come from the price
snapshot and the actor from the session.

`POST /api/payments/{id}/cash/confirm` is restricted to `Administrator`, the
provisional authorized actor until OQ-013 (issue #2) says who receives cash; a
Resident gets `403`. It approves the payment, records the confirming user and
time, and asks Reservations to confirm. Repeating it changes nothing. If the
reservation already expired or was cancelled the payment is still `Approved`
but reports `reservationOutcome: "ApprovedAfterExpiry"` (or
`"ApprovedForCancelledReservation"`) with `requiresManualReview: true`, and the
reservation is not revived. `GET /api/payments/{id}` now also returns `method`
and, for cash, `cashConfirmedAtUtc`.

### Administrative operations (issue #26)

All under `/api/admin/...`, `Administrator` only (Resident 403); the actor comes
from the session. Administration delegates to the module that owns the data.
Page size defaults to 50, max 100.

```http
GET  /api/admin/reservations?buildingId=&status=&useType=&fromUtc=&toUtc=&membershipId=&page=&pageSize=
GET  /api/admin/reservations/{id}
POST /api/admin/reservations/{id}/cancel        { "reason": "..." }
POST /api/admin/reservations/{id}/reschedule    { "startsAtUtc": "...", "endsAtUtc": "...", "reason": "..." }
GET  /api/admin/payments?buildingId=&method=&status=&requiresManualReview=&reservationId=&fromUtc=&toUtc=
GET  /api/admin/pricing/rules?buildingId=&amenityId=&activeAtUtc=
POST /api/admin/pricing/rules                    { buildingId, amenityId, componentType, useType, currency, amount, effectiveFromUtc?, effectiveToUtc? }
GET  /api/admin/amenities/{id}/availability?buildingId=
PUT  /api/admin/amenities/{id}/availability      { buildingId, windows: [{ dayOfWeek, startTime, endTime }] }
POST /api/admin/amenities/{id}/unavailable-periods   { buildingId, startsAtUtc, endsAtUtc, reason? }
DELETE /api/admin/amenities/{id}/unavailable-periods/{periodId}?buildingId=
GET  /api/admin/buildings/{buildingId}/event-slots
POST /api/admin/buildings/{buildingId}/event-slots  { name, startTime, endTime }
PUT  /api/admin/event-slots/{id}                    { name, startTime, endTime }
POST /api/admin/event-slots/{id}/deactivate | /activate
```

- **Cancel** (`Pending`/`Confirmed` only; `Expired` 409; repeating is a no-op)
  never touches a payment: no refund exists yet (OQ-011). The reservation detail
  shows `requiresFinancialReview` when an approved payment sits on a cancelled
  reservation or a payment needs manual review.
- **Reschedule** (`Confirmed`, or `Pending` with an active hold) moves only the
  time range, validated with the same rules as creation and excluding itself;
  resources, price snapshot and `expiresAtUtc` do not change.
- **Price rules** are effective-dated: a new rule supersedes the open one; no
  backdating, no ambiguous overlaps, existing reservation prices never change.
- **Availability and Event slots** shape future bookings only; slots are
  deactivated, never deleted. Times are `HH:mm:ss`.
- Every change is audited in the same transaction (`GET /api/admin/audit`).

### Audit trail (issue #27)

```http
GET /api/admin/audit
```

Administrator only (a Resident gets `403`). Read-only: the trail is append-only
and has no write endpoints. Query parameters (all optional): `buildingId`,
`actorUserId`, `action`, `targetType`, `targetId`, `fromUtc` (inclusive),
`toUtc` (exclusive), `page` (default 1), `pageSize` (default 50, max 100).
Results are newest first:

```json
{
  "items": [
    {
      "id": "...", "occurredAtUtc": "...", "buildingId": "...",
      "actorType": "User", "actorUserId": "...",
      "action": "CashPaymentConfirmed",
      "targetType": "Payment", "targetId": "...",
      "correlationId": "...",
      "metadata": { "method": "Cash", "amount": 5000, "currency": "ARS", "reservationOutcome": "ReservationConfirmed" }
    }
  ],
  "page": 1, "pageSize": 50, "totalCount": 1
}
```

Recorded: login/logout, reservation created/confirmed/expired, Mercado Pago
initiated/approved/rejected/cancelled, cash declared/confirmed, payments
requiring manual review and processed webhooks. Each is written atomically with
the transition it describes and never duplicated by an idempotent repeat.
Payments needing manual review: `?action=PaymentRequiresManualReview`. Metadata
is allowlisted and never contains secrets, tokens or e-mails. Retention is not
implemented yet. Design: `docs/04-data/domain-model.md#audit-trail-issue-27`.

Tests use an in-memory fake provider; no credentials or network are needed.
Design, race handling and signature algorithm:
`docs/04-data/domain-model.md#payments-and-mercado-pago-issue-24`.

## Run

```bash
dotnet run --project apps/api/ResidentialAmenities.Api.csproj
```

Development URLs:

- Health: `http://localhost:8080/health`
- Compatibility health: `http://localhost:8080/api/health`
- OpenAPI: `http://localhost:8080/openapi/v1.json`

OpenAPI is exposed only in Development.

## Reset local database

```bash
docker compose down -v
docker compose up -d postgres
dotnet ef database update \
  --project apps/api/ResidentialAmenities.Api.csproj \
  --context AppDbContext
```

This is destructive and intended only for local development.
