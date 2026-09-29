# Client

Angular + Ionic + Capacitor client for Residential Amenities Platform.

## Status

This is no longer an early scaffold: the client implements the full resident
and administrator product surface through issue #55 — web authentication,
resident amenity/availability browsing, both reservation flows, payment,
resident reservation history/detail, and the complete administrator
workflow, plus a testing/accessibility/error-handling hardening pass.

**Resident** (issues #44–#50): a real application shell, a small design
system, a typed API client/error model/endpoint catalog, cookie-backed web
login/logout, session bootstrap, route guards, global 401 handling, a
resident building context derived from the user's own memberships, browsing
of a building's amenities and their structural availability, creating a
Shared/Exclusive Leisure reservation hold or an Event reservation (configured
slots + Pool/Barbecue add-ons), paying that hold — Mercado Pago Checkout Pro
or a cash declaration, with a server-verified return screen and
manual-review/cash-pending states shown honestly — and a resident-facing
reservation history list/detail view.

**Administrator** (issues #51–#54): read dashboards (overview, reservations,
payments), reservation cancel/reschedule with an explicit confirm step,
price-rule/availability/maintenance-period/event-slot configuration, the
read-only audit trail, and manual payment review/cash confirmation — all
under `/admin`, gated by `adminGuard`.

**Hardening** (issue #55): a shared `AppErrorStateComponent`
(`shared/error-state`) unifying error rendering across resident and admin
screens, an accessibility pass (labels, button copy, `role="alert"`), a
responsive review of admin forms/nav, and a full-flow integration test (see
"Shared error-state component" and "Testing" below). See
[`docs/12-roadmap/backlog-mvp-client.md`](../../docs/12-roadmap/backlog-mvp-client.md)
for the full backlog.

## Baseline

- Angular 22 standalone application
- Ionic 9
- Capacitor 8
- Angular Router
- Typed API client, error model and endpoint catalog (`core/api`)
- Web authentication/session state (`core/auth`)
- Resident building context derived from the session's own memberships
  (`core/resident-context`)
- A reusable page shell (`layout/app-shell`)
- A neutral product landing page (`features/landing`)
- A web login page (`features/login`)
- Resident amenity listing and structural-availability browsing
  (`features/resident/amenities`)
- Shared/Exclusive Leisure reservation quote-and-create flow
  (`features/resident/reservations/leisure`)
- Event reservation quote-and-create flow (`features/resident/reservations/event`)
- Payment (Mercado Pago Checkout Pro + cash) (`features/resident/payments`)
- Resident reservation history list/detail (`features/resident/reservations/history`)
- The full administrator workflow (`features/admin`): overview/reservations/
  payments dashboards, cancel/reschedule, pricing, availability, event slots,
  audit, and manual payment review/cash confirmation
- A shared, reusable error-state component (`shared/error-state`)
- An internal `/health` diagnostic (`features/diagnostics`, `core/health`)

## Install and run

From `apps/client`:

```bash
npm install
npm start
```

Open `http://localhost:8100`.

## Node version

This project requires **Node `>=24.15.0 <25`** (see `engines` in
`package.json`) — the Angular 22 CLI itself refuses to run on an older
patch of Node 24. If your local Node is older (e.g. `24.11.x`), install a
compatible version with your version manager of choice (nvm, fnm, Volta) —
do not lower this requirement to work around a local toolchain. CI
(`.github/workflows/ci.yml`) always uses a compatible Node 24.x via
`actions/setup-node`, so `npm test`/`npm run build` are validated there even
when a local machine cannot run them.

## Build

```bash
npm run build
```

## Test

The Angular 22 client uses the Angular CLI unit-test builder with Vitest and
jsdom.

```bash
npm test
```

Tests run once by default through the repository script, which is suitable
for local validation and CI. No test calls a real backend: HTTP-level tests
use Angular's `HttpTestingController`.

`src/app/integration/resident-cash-payment-flow.spec.ts` (#55) is a
full-flow integration spec using `RouterTestingHarness` + real
`app.routes.ts` + `HttpTestingController` (no mocked services): login →
session bootstrap → load amenities → create a Leisure reservation → declare
a cash payment. It asserts a declared-but-unconfirmed cash payment is never
shown or treated as a confirmed reservation (Cash Pending ≠ Reservation
Confirmed — see "Payment flows" above).

**Known local limitation:** local Node was `v24.11.1` during #55 while the
Angular 22 CLI requires `>=24.15.0`; `npm test`/`npm run build` could not run
locally in that environment (see "Node version" above). `npx tsc -p
tsconfig.app.json --noEmit` and `npx tsc -p tsconfig.spec.json --noEmit`
were used instead to validate every change, and CI remains authoritative for
the actual `ng test`/`ng build`.

## Client structure

```text
src/
├── app/
│   ├── core/            # singleton infrastructure — no feature imports it back
│   │   ├── api/         # ApiClient, ApiError, apiPaths, query-param helper
│   │   ├── auth/        # web session store/service/guards/401 interceptor
│   │   ├── config/      # API_BASE_URL token
│   │   ├── resident-context/ # active building derived from memberships
│   │   └── health/      # HealthService (root-level /health, not /api)
│   ├── layout/           # reusable page chrome
│   │   └── app-shell/    # header + content frame every page composes
│   ├── shared/           # reusable UI shared across features (not page chrome)
│   │   └── error-state/  # AppErrorStateComponent — unified ApiError rendering (#55)
│   ├── features/         # product screens, one folder per feature
│   │   ├── landing/       # authenticated landing page
│   │   ├── login/         # public web login
│   │   ├── resident/
│   │   │   ├── amenities/    # amenity listing + availability browsing (#46)
│   │   │   ├── reservations/
│   │   │   │   ├── reservation.models.ts # DTOs shared by Leisure and Event
│   │   │   │   ├── leisure/  # Shared/Exclusive Leisure quote+create (#47)
│   │   │   │   ├── event/    # Event slots + add-ons quote+create (#48)
│   │   │   │   └── history/  # reservation list/detail (#50)
│   │   │   └── payments/     # Mercado Pago / cash payment (#49)
│   │   ├── admin/         # administrator workflow (#51-#54)
│   │   │   ├── admin-shell.page.ts, admin-overview.page.ts
│   │   │   ├── reservations/  # list/detail, cancel/reschedule (#52)
│   │   │   ├── payments/      # list + cash confirmation (#54)
│   │   │   ├── pricing/       # price-rule configuration (#53)
│   │   │   ├── availability/  # windows + maintenance periods (#53)
│   │   │   ├── event-slots/   # event slot configuration (#53)
│   │   │   ├── audit/         # read-only audit trail (#54)
│   │   │   └── payment-review/ # manual review queue (#54)
│   │   └── diagnostics/   # internal /health check, not linked from nav
│   ├── integration/       # cross-feature integration specs (#55)
│   ├── app.component.ts   # root: <ion-app><ion-router-outlet></ion-router-outlet></ion-app>
│   ├── app.config.ts
│   └── app.routes.ts
├── theme/
│   └── tokens.scss        # design tokens (spacing, radius, focus, …)
├── environments/
├── main.ts
└── styles.scss
```

`core` never imports from `features`.

### Adding a feature

1. Create `features/<name>/<name>.page.ts` as a standalone component; lazy-load
   it from `app.routes.ts` with `loadComponent`.
2. Compose its content inside `<app-shell title="...">…</app-shell>`
   (`layout/app-shell`) instead of writing a new `ion-header`/`ion-content`.
3. Call the backend only through `ApiClient` + `apiPaths` (below) — never a
   hardcoded URL.
4. Add a spec next to the component.

### Adding an API endpoint to the catalog

`core/api/api-paths.ts` is the **only** place backend paths are written.
Before adding an entry, confirm the route actually exists in `apps/api`
(`Modules/*/​*Endpoints.cs` or the generated OpenAPI document) — this catalog
must never get ahead of the backend. Paths are **relative** (no leading
`/api`): `ApiClient` already resolves against `API_BASE_URL`, so
`apiPaths.auth.login = '/auth/login'` becomes a request to `/api/auth/login`.
Prefixing an entry with `/api` here would produce `/api/api/...`.

## API client and error handling

`ApiClient` (`core/api/api-client.service.ts`) wraps Angular's `HttpClient`
with `get`/`post`/`put`/`delete`, generic response/body types, and optional
query parameters (`ApiQueryParams`, built into `HttpParams` — array values
are repeated once per item, `undefined`/`null` are omitted). Every method
resolves its `path` against `API_BASE_URL` (same-origin `/api`).

Every failed call rejects with a structured `ApiError`
(`core/api/api-error.ts`) instead of a raw `HttpErrorResponse`:

```ts
interface ApiError {
  status: number; // 0 when the request never reached the server
  title: string;
  detail?: string;
  type?: string;
  instance?: string;
}
```

- A backend `ProblemDetails` body populates `status`/`title`/`detail`/`type`/`instance`.
- A network failure (`status === 0`) becomes a safe "could not reach the
  server" error — never the raw browser error event.
- Any other unexpected response (e.g. a 500 with an HTML body) becomes a
  safe generic fallback by status code.

No path in this mapping ever surfaces a stack trace, an exception message, a
connection string or a raw server object to the UI, and nothing here
`console.log`s an error by default.

## Shared error-state component

Issue #55 introduced `AppErrorStateComponent`
(`shared/error-state/app-error-state.component.ts`) as the one place that
renders an `ApiError` (or a plain validation string) to the resident/admin —
replacing the many hand-written
`<p role="alert"><ion-text color="danger">{{ ... }}</ion-text></p>` blocks
that had accumulated across `features/resident` and `features/admin`:

```html
<app-error-state
  [title]="errorTitle()"
  [detail]="errorDetail() ?? undefined"
  [showRetry]="true"
  (retry)="reload()"
/>
```

- `title` (required) and `detail` (optional) are rendered **verbatim** —
  never clipped, never conditioned on status code. A migrated screen always
  shows the exact same real `ApiError.title`/`.detail` it showed before.
- `showRetry` defaults to `false`; a screen opts in only where retrying was
  already safe/offered before the migration (a `GET` reload). It is never
  turned on for a non-idempotent mutation that didn't have a retry action
  already — reservation/event-reservation creation still have none.
- The message renders with `role="alert"`, matching every error block it
  replaced.
- Adopted across both resident (`login`, `payments/payment.page`,
  `reservations/leisure`, `reservations/event`) and administrator
  (`audit`, `payments`, `availability`, `event-slots`,
  `reservations/admin-reservation-detail`, `pricing`) screens — not every
  single error render in the app, but a representative, broad set covering
  cancel/reschedule, pricing, availability, event-slots and cash-confirm.
- Unit-tested directly (`app-error-state.component.spec.ts`): renders the
  title with `role="alert"`, renders/omits `detail`, shows the retry button
  only when `showRetry` is set, and emits `(retry)` on click.

## Web authentication

Issue #45 implements browser/web authentication against the backend's
ASP.NET Core Identity cookie flow.

### Flow

- `POST /api/auth/login?useCookies=true` sends only `{ email, password }`.
- On a successful login response, the backend sets an HttpOnly session
  cookie (`residential-auth` in Development, `__Host-residential-auth` in
  non-Development).
- The client immediately calls `GET /api/auth/me`; only that response marks
  the session as authenticated and provides the visible user context.
- On application startup, an app initializer calls `GET /api/auth/me` again
  to restore the in-memory session from the browser-managed cookie.
- `POST /api/auth/logout` signs out server-side; the client then clears only
  its in-memory session state and navigates to `/login`.

The client never reads, writes or copies cookies. It also never stores a
token, password, role, user object, membership or auth boolean in
`localStorage`, `sessionStorage`, IndexedDB, Ionic Storage or Capacitor
Preferences. Persistence across reloads comes only from:

```text
HttpOnly cookie managed by the browser
+
GET /api/auth/me at startup
```

The one exception anywhere in the client is issue #49's
`MercadoPagoReturnContextStore`, which uses `sessionStorage` for exactly two
non-sensitive UUIDs (`paymentId`, `reservationId`) needed to correlate the
browser's return from Mercado Pago's Checkout Pro — see "Payment flows"
below. This is not session persistence: it never carries a token, cookie,
role, user object or membership, and reading it never substitutes for
`GET /api/auth/me`.

### Session state

`core/auth/auth-session.store.ts` owns session state only. It has no
`HttpClient` dependency and keeps a discriminated state in memory:

```ts
type AuthState =
  | { status: 'checking' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; user: CurrentUser };
```

`CurrentUser` is populated only from `/auth/me`:

```ts
interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  roles: ('Resident' | 'Administrator')[];
  memberships: ResidentMembershipContext[];
}
```

`Administrator` is treated as satisfying resident-level UX access because
the backend's `ResidentAccess` policy accepts either role. The client does
not infer a resident membership from the Administrator role; memberships are
the array returned by `/auth/me`, and building selection is deferred to the
resident product slice.

### Guards and 401 handling

- `/login` is public and redirects an already-authenticated user to `/`.
- `/` requires an authenticated session.
- `/admin/*` is protected by an Administrator guard. There is intentionally
  no placeholder admin dashboard yet.
- Guards wait for the idempotent session initializer before deciding, so
  protected content is not rendered while `/auth/me` is still resolving.
- Anonymous redirects may include a sanitized internal `returnUrl`. Absolute
  URLs, protocol-relative URLs and `/login` itself are rejected to avoid open
  redirects.

`core/auth/auth.interceptor.ts` handles protected-resource `401` responses:
it clears the in-memory session and navigates to `/login`. It deliberately
does **not** treat `403` as an expired session.

The following auth endpoints are excluded from the automatic redirect:

- `/auth/login`: invalid credentials also return `401`, and the login page
  must show its own generic error without a redirect loop.
- `/auth/me`: startup may legitimately return `401` when no cookie exists.
- `/auth/logout`: an already-expired logout is handled by `AuthService`.
- `/auth/refresh`: reserved for bearer/native flows; not used by web auth.

### Native/Capacitor auth status

This issue implements web cookie authentication only. The security direction
for native/non-browser clients remains opaque bearer access tokens plus
refresh tokens stored in secure platform storage, but that flow is not
implemented here. Do not treat Capacitor Android/iOS authentication as
release-ready until a dedicated native bearer + secure-storage issue is
planned and implemented.

## Resident context and amenity/availability browsing

Issue #46 lets an authenticated resident (or an Administrator who also holds
a resident membership) explore their building's amenities and each
amenity's structural availability. It does not create reservations — that
arrives with #47/#48.

### Active building context

`core/resident-context/resident-context.store.ts` (`ResidentContextStore`)
derives the active building **only** from `AuthSessionStore.memberships()` —
the array already returned by `GET /api/auth/me`. It never makes a second
request to reconstruct memberships and never invents an endpoint for it.

- **0 memberships**: `activeMembership` is `null`. No amenities request is
  made; the UI shows "No tenés una membresía residencial activa disponible."
  — not an authentication error, since the user is genuinely signed in.
- **1 membership**: it is auto-selected; no building selector is shown.
- **N memberships**: nothing is picked arbitrarily. `activeMembership` stays
  `null` (`requiresSelection()` is `true`) until the resident calls
  `selectBuilding(buildingId)`.

`selectBuilding` only accepts a `buildingId` that already appears in the
user's own `memberships()`; anything else is rejected, the context is left
untouched and no request is made. This is UI-level defense in depth only —
the backend's `IBuildingMembershipAuthorizer` remains the real authority and
can still return `403` even when the UI thought a building was valid (a
stale/edge-case scenario the amenities list explicitly handles).

The active selection is **in-memory only** — never written to
`localStorage`/`sessionStorage`/IndexedDB/Capacitor Preferences. A reload
with a single membership reconstructs itself from `/auth/me`; a reload with
several asks the resident to choose again.

A selection is scoped to the identity of the user who made it, not just to
a `buildingId`: internally it is stored as `{ userId, buildingId }`, and
`activeMembership` only reads it back when `selection.userId` matches the
currently signed-in user's id. This makes it impossible by construction for
one user's selection to reactivate for a different user, even if both
happen to hold a membership with the same `buildingId` — storing the raw
`buildingId` alone would not guarantee that. `activeMembership` is fully
derived from `memberships()` plus that identity-scoped selection, so login,
logout, switching user and the `/auth/me` restore after reload all resolve
correctly without any explicit reset:

- a previously selected building id that no longer belongs to the current
  memberships simply stops resolving to a membership;
- a selection made by a *different* user never resolves for the current
  one — logging out and a different user logging back in always requires a
  fresh, explicit selection when they have more than one membership, even
  if the previous user had selected a building the new user also belongs
  to.

### Amenities and availability

- `features/resident/amenities/amenities.models.ts` — `AmenitySummary` and
  `AvailabilityInterval`, typed exactly to the backend's
  `AmenitySummaryResponse`/`AvailabilityIntervalResponse`
  (`apps/api/Modules/Amenities/AmenityEndpoints.cs`). No invented fields
  (price, capacity, image, description, next available slot, maintenance
  reason, …) — the endpoints do not return them.
- `features/resident/amenities/amenities.service.ts` — `AmenitiesService`
  wraps `ApiClient` with `listForBuilding(buildingId)` and
  `getAvailability(amenityId, fromUtc, toUtc)`, using only
  `apiPaths.amenities.listForBuilding`/`apiPaths.amenities.availability`.
- `features/resident/amenities/amenities.page.ts` — the routed `/amenities`
  page: building selector (when needed), amenity list
  (loading/empty/success/error with retry), and amenity selection.
- `features/resident/amenities/amenity-availability.component.ts` — once an
  amenity is selected, lets the resident pick a `fromUtc`/`toUtc` range and
  view the backend's raw open intervals, grouped by local calendar day.

**Structural availability, not a promise of a free slot.** The backend's
`AmenityAvailabilityCalculator` only subtracts recurring windows and
maintenance/unavailable periods — it does **not** consult existing
reservations. This client never claims otherwise: the UI's copy says the
schedule is "according to configuration" and that final availability is
validated when a reservation is confirmed, and it never uses wording like
"free slot guaranteed" or "reserve now". An empty `[]` response is rendered
as "No hay franjas habilitadas para este rango." — not an error. A gap
between open intervals is shown simply as not available; it is never
labeled "Mantenimiento" (or any other cause), because the endpoint does not
return *why* a gap exists.

**No client-side availability computation.** The client only performs
`request → response → render`; it never re-implements weekly windows,
maintenance subtraction, timezone rules or reservation overlap in
TypeScript — the backend remains the sole authority (RNF-004).

### Query range

`features/resident/amenities/availability-range.ts` mirrors the backend's
`AmenityAvailabilityCalculator.MaxQueryRange` (62 days) and validates a
range client-side before it is ever sent — end after start, and no more than
62 days — purely so the UI never fires a request the backend would reject
anyway; the backend still re-validates and remains authoritative. The
default initial browsing window is the next 7 days, and is just a display
starting point, not a business rule.

### Time zone

The backend computes structural availability using `Building.TimeZoneId`,
but neither `/auth/me` nor the amenities endpoints expose that time zone to
this client today, and availability responses are UTC timestamps. This
client does **not** hardcode `America/Argentina/Buenos_Aires` or assume
every building is in Buenos Aires. Instead, it renders availability
timestamps using the browser/device's local time zone (`Intl.DateTimeFormat`
with no explicit `timeZone`, `Date` parsing/formatting only — no custom
offset math).

**Known gap:** this is the device's local time zone, not necessarily the
building's authoritative time zone, so a resident browsing from a different
time zone than their building would see availability windows labeled by
their own local day/time rather than the building's calendar day. Closing
this gap correctly needs the backend to expose `Building.TimeZoneId` (e.g.
on `/auth/me`'s membership entries or the amenity/availability responses).
That is out of scope for #46 and is not solved with a client-side constant;
a small, separate backend issue is recommended before a flow that depends on
calendar-day-accurate building-local time.

### Race conditions

Both the amenities list and the availability lookup key their request on an
Angular signal (`ResidentContextStore.activeBuildingId` / the selected
amenity) piped through RxJS `switchMap`, so a late response from a request
made against a building or amenity the user has since navigated away from
is cancelled and never overwrites the current view. Selecting a different
amenity or building also clears the previous selection/availability state
immediately, before any new request is made.

## Leisure reservation flow (Shared/Exclusive)

Issue #47 adds the first flow that actually creates a reservation:
`SharedLeisure`/`ExclusiveLeisure` against `POST /api/reservations`. It ends
when a `Pending` hold exists — Event reservations (#48), payment (#49) and
admin cancel/reschedule (#52) are all out of scope.

`features/resident/reservations/leisure/` embeds directly under the
selected amenity on `/amenities` (no new route): `LeisureReservationComponent`
takes the already-loaded `AmenitySummary` as its `amenity` input, and reads
`buildingId` from `ResidentContextStore.activeBuildingId()` — the same
identity-scoped source #46 established. Neither id is ever an editable
field, so there is no way for a resident to type an arbitrary
building/amenity id into this flow.

### Use type

`LeisureUseType` is a closed union (`'SharedLeisure' | 'ExclusiveLeisure'`),
never a free `string`. Which options are offered is derived only from the
selected amenity's own flags:

- only `allowsSharedUse`: `SharedLeisure` is used, no selector shown;
- only `allowsExclusiveUse`: `ExclusiveLeisure` is used, no selector shown;
- both: the resident must choose explicitly — nothing is auto-picked;
- neither: no functional reservation form renders at all ("Esta amenity no
  admite reservas de ocio."), and no request the backend would reject is
  ever sent.

### Quote, then create — two separate requests, never bound together

`GET /api/pricing/quote` (`buildingId`, `amenityId`, `useType` — never
add-ons, never a membership id) must succeed before the "Confirm
reservation" button is enabled at all. But quoting and creating are
independent requests: `POST /api/reservations` recalculates the price at
the instant it runs, so the client never claims the quoted price is
"locked in" or "guaranteed". If a price rule changes between the two calls,
the confirmation screen shows the real amount from the `201` response, and
if it differs from the quote it says so explicitly ("El precio se actualizó
al crear la reserva. Este es el importe registrado en el hold.") — never
silently, and never with an automatic rollback (there is no resident-facing
cancellation endpoint to roll back to; see "Known gaps" below). Changing
the use type invalidates a previous quote (it priced a different use type);
changing the date range does not, since the current backend pricing model
does not vary by duration (`Base` component only — this client never
multiplies price by hours).

### What the client sends and shows

The create request is exactly:

```ts
{ buildingId, amenityId, useType, startsAtUtc, endsAtUtc }
```

Never `membershipId` (resolved server-side from the authenticated caller),
never a price/currency/status field, and never `addOnAmenityIds` (Event
only). The confirmation view renders only fields the `201`
`ReservationResponse` actually returned — `status`, `totalAmount`,
`currency`, `expiresAtUtc` — none of them assumed or computed client-side.
In particular, the hold's expiry is shown as the literal `expiresAtUtc`
timestamp; this client never assumes or displays a fixed hold duration
(e.g. "30 minutes"), because that duration is a backend configuration value
that can change.

### Errors and network uncertainty

- `400` (invalid range/use type) and `422` (outside availability,
  amenity/use-type mismatch, no active price rule) show the backend's own
  safe `ApiError` title/detail — this client does not assume a single cause
  for `422`.
- `409` (an incompatible reservation already occupies that time) shows a
  clear "the time range is no longer available" message and leaves no
  partial/phantom reservation state.
- `403` shows an access-denied message; it never signs the resident out
  (only the global `401` interceptor does that).
- A network failure (`ApiError.status === 0`) during **create** is shown as
  an explicitly uncertain result ("No pudimos confirmar el resultado de la
  creación. Evitá repetir inmediatamente la operación.") — the client
  cannot tell whether the server never received the request or created the
  hold and lost the response, so it never claims the reservation definitely
  was not created. A quote failure, by contrast, is safely retryable (`GET`,
  no state created), and the UI offers a manual retry there.

### No double submit, no automatic retry

`POST /api/reservations` has no idempotency key, and two identical
`SharedLeisure` requests are both individually valid (Shared+Shared is
compatible), so a duplicate submit could create two separate holds. The
submit button is disabled while a create request is in flight, and
`create()` itself also guards against a second call while one is already
pending. There is no `retry`/`retryWhen` anywhere in the create path —
after a `400`/`409`/`422`, the resident edits the form and resubmits
manually; after a network failure, resubmitting is the resident's own
explicit choice, made with the uncertainty above visible.

### Race conditions and confirmed holds

The quote request is re-issued only on an explicit "Get quote" click, so it
never fires on every keystroke. Switching to a different amenity (parent
swaps `[amenity]`) or building (parent tears the whole selected-amenity
section down) clears any in-progress quote/creation state immediately —
a stale quote or hold confirmation for a resource the resident navigated
away from is never left on screen. Once a hold is confirmed, the
confirmation view is the only thing rendered until the resident takes the
explicit "Create another reservation" action; an incidental form edit can
never reinterpret an already-created hold as something else.

### Time zone (same documented gap as #46)

Like `/amenities`, this flow renders `<input type="datetime-local">` in the
browser/device's local time zone and converts to UTC via
`Date.toISOString()` — no custom offset math, and no hardcoded
`America/Argentina/Buenos_Aires`. The range control reuses
`features/resident/amenities/availability-range.ts` (end-after-start, 62-day
maximum, mirroring `AmenityAvailabilityCalculator.MaxQueryRange`, which
`ReservationScheduleValidator` also reuses server-side) instead of
duplicating that logic. The building's authoritative time zone is still not
exposed to this client — see the gap already documented under "Resident
context and amenity/availability browsing" above.

### Known contract gaps (not solved client-side)

- **No creation idempotency key.** `POST /api/reservations` cannot
  distinguish a genuine retry from a new request; this client compensates
  only at the UX level (disabled submit, no auto-retry), which reduces but
  does not eliminate the double-hold risk under a real network failure.
- **The quote is not bound to the create.** Nothing locks the price
  between the two requests; this is a deliberate simplification of the
  current backend and is surfaced to the resident rather than hidden.
- **No resident-facing cancellation.** If a `201` hold's price differs from
  its quote, there is no endpoint this client can call to cancel it, so the
  UI cannot offer a rollback action even if it wanted to.
- **Building time zone** — unchanged gap from #46.
- **No "my reservations" view yet (#50).** This flow does not attempt to
  list or look up existing reservations; the `201` response is the only
  source of truth this screen ever shows.

## Event reservation flow (slots + add-ons)

Issue #48 adds `UseType=Event` on top of the resident-facing slot-discovery
contract from issue #62. `EventReservationComponent` renders under a
selected amenity only when `amenity.kind === 'Sum'`, alongside
`AmenityAvailabilityComponent` and `LeisureReservationComponent` — the same
SUM keeps its existing Leisure flow. It is given the full, already-loaded
building amenities list (`AmenitiesPage.amenitiesList()`) as add-on
candidates, never issuing a second amenities request.

- **Slot discovery**: `GET /api/buildings/{buildingId}/event-slots?date=`
  (issue #62). The resident picks a plain calendar date
  (`<input type="date">`, `YYYY-MM-DD`) and only ever selects one of the
  `EventSlotOccurrence`s the backend actually returns — never a free
  start/end range. `startsAtUtc`/`endsAtUtc` on the selected occurrence are
  sent to `POST /api/reservations` **byte-for-byte**, never reconstructed
  from the chosen date with `new Date(...)`/`toISOString()` or any
  device-timezone math. Since the client still doesn't know
  `Building.TimeZoneId` (same gap as #46/#62), the confirmed slot is
  identified to the resident as "chosen date + slot name" — never
  re-rendered as if the raw UTC instants were the building's local time.
- **Add-ons**: eligible candidates are derived only from the real,
  already-loaded amenities list — `kind === 'Pool' || kind === 'Barbecue'`,
  `allowsExclusiveUse === true`, and not the base SUM itself — mirroring
  `ReservationCreationService`'s actual invariants. There is **no** hardcoded
  maximum count; the backend only limits by type/duplicates/ownership/
  exclusive-use, not a number, so any quantity of eligible amenities can be
  selected. If the amenities list changes and a selected add-on is no longer
  eligible, it's silently dropped from the selection (and the quote
  invalidated) rather than kept as a stale id.
- **Base SUM eligibility**: Event also requires the base amenity to allow
  exclusive use (`RB-006`); when it doesn't, the component shows a neutral
  "Este SUM no admite reservas de tipo Event." instead of a functional form
  that would only fail at create time.
- **Quote**: `GET /api/pricing/quote` with `buildingId`, `amenityId` (the
  SUM), `useType=Event`, and `addOnAmenityId` repeated once per selected
  add-on (via `ApiQueryParams`' array support — never a hand-built query
  string or CSV). No add-ons selected means no `addOnAmenityId` at all.
  Exactly like #47: quote and create are separate requests, the `201`'s
  `totalAmount`/`currency`/`priceLines` are the only final price truth, and
  a difference from the quote is surfaced ("El precio se actualizó al crear
  la reserva…"), never hidden or rolled back automatically.
- **Create**: `{ buildingId, amenityId, addOnAmenityIds, useType: 'Event',
  startsAtUtc, endsAtUtc }` — never `membershipId`, the slot's own `id`, or
  any price/status field.
- **Context invalidation**: reuses #47's `contextVersion` approach. Changing
  the base SUM, date, selected slot, or add-on selection all invalidate any
  in-flight/previous quote and bump the version a quote/create request
  captures; a late response is discarded once the version has moved on.
  Slot discovery itself is a safe `GET`, so it's keyed through
  `switchMap` (a genuinely stale date's response is cancelled outright);
  `POST /api/reservations` is never wrapped in `switchMap`/`retry` — it
  stays an explicit, single action, and a network failure
  (`ApiError.status === 0`) is shown as an uncertain result exactly like
  #47, blocking a second create for that context rather than allowing a
  duplicate hold against a non-idempotent endpoint.
- **Full-day**: not offered as an option — out of MVP scope per issue #2,
  and the domain doesn't support it regardless.
- **Payment**: out of scope (#49) — the confirmation only says "El pago se
  realizará en el siguiente paso."

Shared reservation DTOs (`PriceQuote`, `Reservation`, etc.) live in
`features/resident/reservations/reservation.models.ts`, used by both the
Leisure and Event services/components instead of being duplicated.

## Payment flows (Mercado Pago + cash)

Issue #49 pays the `Pending` hold #47/#48 create. Both reservation flows'
confirmation card gets a **Continuar al pago** link to
`/reservations/{reservation.id}/payment` — no price, status or other data is
passed via query params; `PaymentPage` always re-fetches the reservation from
`GET /api/reservations/{id}`, so it is independently reachable (reload,
direct navigation, a login redirect's `returnUrl`) and never depends on a
signal a previous component left behind.

- **Reservation load**: `PaymentPage` reads `reservationId` from the route
  param (not from memory) and shows `loading`/`success`/`error` for it. A
  reservation whose `status` is not `Pending` shows a neutral message instead
  of payment actions — but the client never treats the countdown reaching
  zero as authoritative: the hold's `expiresAtUtc` only drives a **display**
  countdown, and a `POST` past that instant is left to the backend to accept
  or reject. The device clock is never used to locally mark a reservation
  `Expired`.
- **Mercado Pago**: `POST /api/reservations/{id}/payments/mercadopago` with
  an empty (`null`) body — the backend derives amount/currency from the
  reservation's own price snapshot and manages its own idempotency key. On
  success, only `{ paymentId, reservationId }` — never `checkoutUrl`,
  `providerOrderId`, tokens or credentials — is saved via
  `MercadoPagoReturnContextStore` (`sessionStorage`, key
  `residential-amenities:mercadopago-return`), then `ExternalNavigationService`
  redirects the same tab to `checkoutUrl` (same-tab, not `window.open`, so the
  `sessionStorage` entry survives for the return page).
- **Cash**: `POST /api/reservations/{id}/payments/cash` with an empty body;
  the response is intentionally minimal, so the page immediately follows up
  with `GET /api/payments/{paymentId}` to render the full read model. A
  `Pending` cash payment is shown with an explicit "todavía NO confirma la
  reserva" notice — confirming receipt is `POST /api/payments/{id}/cash/confirm`,
  an **Administrator-only** endpoint this resident-facing flow never calls.
  A manual "Actualizar estado" re-runs the same `GET` so the resident can
  check for themselves once an administrator confirms.
- **Method conflict**: declaring one method while the other is already active
  is a real backend `409`, shown with its own title/detail — the client never
  switches methods, replaces the existing attempt, or guesses a cause.
- **Manual retry, never automatic**: neither POST is wrapped in
  `retry`/`retryWhen`. Unlike reservation creation (#47/#48), both are
  backend-idempotent — Mercado Pago reuses the same persisted attempt and
  idempotency key, Cash returns the same still-`Pending` declaration — so a
  network failure (`status === 0`) is shown as a retryable message, and the
  action is immediately clickable again rather than permanently blocked.
  Both actions are still disabled from double-submitting and from each other
  while either is in flight.
- **Payment status rendering**: `Payment.status` (`Created`/`Pending`/
  `Approved`/`Rejected`/`Cancelled`) and `Payment.requiresManualReview` are
  rendered from **exhaustive, typed** mappings — never a free string, and
  never a synthetic "RequiresManualReview" status. `requiresManualReview` is
  its own boolean, shown as a separate warning alongside an `Approved`
  status when true; `Payment.reservationOutcome` (`None`/
  `ReservationConfirmed`/`ApprovedAfterExpiry`/
  `ApprovedForCancelledReservation`/`ApprovedForMissingReservation`) is
  never inferred from `status` — it is rendered exactly as the backend
  reports it, and "Pago aprobado y reserva confirmada" is only ever shown
  when the backend's own outcome says `ReservationConfirmed`.
- **Return page** (`/payments/return`, `PaymentReturnPage`): **never injects
  `ActivatedRoute` and never reads a query parameter.** Mercado Pago's
  `back_urls` are UX-only configuration (RB-011) and the provider may append
  its own `status`/`payment_id`/`collection_status`/etc. on return, none of
  which this app can trust as identity or proof of payment. The only
  `paymentId` this page will ever query is the one read once, at
  construction, from `MercadoPagoReturnContextStore` — a visitor with no
  saved context (different tab, cleared session, a bare bookmark) sees a
  neutral "No encontramos un pago iniciado en esta sesión." and the page
  makes no request at all. A manual "Actualizar estado" re-queries the same
  stored id; there is no background polling.
- **Amount/currency**: `PaymentPage`'s initial snapshot uses
  `Reservation.totalAmount`/`currency`; once a `Payment` exists, its own
  `amount`/`currency` are used instead — nothing is recalculated or
  re-quoted client-side.

### Native/Capacitor authentication remains a gap

The client's web session (issue #45) is entirely cookie-based
(`docs/06-security/security-baseline.md`'s Web posture); the same doc's
Mobile/non-browser posture — an opaque bearer token, refresh flow and secure
device storage — is **not implemented by any issue through #49**. This is a
known gap before a native Android/iOS build (RF-023) can ship: nothing in the
backlog through the current phase covers it. It should be scoped as its own
issue before native packaging work begins, rather than assumed solved.

## `/health` vs `/api`

The backend exposes `/health` as a **root-level operations endpoint**,
outside its `/api` product catalog (`apps/api/Endpoints/HealthEndpoints.cs`;
`/api/health` also exists there today only as a compatibility route for this
client's earlier scaffold — new code should not depend on it). `HealthService`
therefore calls `/health` directly with `HttpClient`, not through `ApiClient`
(which is reserved for `/api/*`). The dev proxy forwards both `/api` and
`/health` to the backend (`proxy.conf.json`).

The health check is an internal diagnostic (`/diagnostics`, not linked from
any product navigation), not the application's landing page.

## Capacitor

Capacitor is initialized but native platform directories are not committed
yet.

After a successful web build:

```bash
npm run cap:add:android
npm run cap:add:ios
npm run cap:sync
```

Android can be developed on supported Android tooling. iOS native builds
require macOS/Xcode.

Publishing/signing remains outside this phase's scope.

Native authentication is also outside this phase's scope; see
"Native/Capacitor auth status" above.
