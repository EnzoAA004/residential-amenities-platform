# Client

Angular + Ionic + Capacitor client for Residential Amenities Platform.

## Status

Frontend foundation, web authentication and resident amenity/availability
browsing (issues #44, #45 and #46): a real application shell, a small design
system, a typed API client/error model/endpoint catalog, cookie-backed web
login/logout, session bootstrap, route guards, global 401 handling, a
resident building context derived from the user's own memberships, and
browsing of a building's amenities and their structural availability.
Reservations and payment arrive with issues #47 and later (see
[`docs/12-roadmap/backlog-mvp-client.md`](../../docs/12-roadmap/backlog-mvp-client.md)).

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
│   ├── features/         # product screens, one folder per feature
│   │   ├── landing/       # authenticated landing page
│   │   ├── login/         # public web login
│   │   ├── resident/
│   │   │   └── amenities/ # amenity listing + availability browsing (#46)
│   │   └── diagnostics/   # internal /health check, not linked from nav
│   ├── app.component.ts   # root: <ion-app><ion-router-outlet></ion-router-outlet></ion-app>
│   ├── app.config.ts
│   └── app.routes.ts
├── theme/
│   └── tokens.scss        # design tokens (spacing, radius, focus, …)
├── environments/
├── main.ts
└── styles.scss
```

`core` never imports from `features`. `features/admin/` arrives with a later
product slice once there is an administrator workflow to render — it is not
created empty ahead of time.

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
several asks the resident to choose again. `activeMembership` is fully
derived from the current `memberships()` signal, so login, logout, switching
user and the `/auth/me` restore after reload all resolve correctly without
any explicit reset: a previously selected building id that no longer
belongs to the current session's memberships simply stops resolving to a
membership.

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
