# Client

Angular + Ionic + Capacitor client for Residential Amenities Platform.

## Status

Frontend foundation plus web authentication (issues #44 and #45): a real
application shell, a small design system, a typed API client/error
model/endpoint catalog, cookie-backed web login/logout, session bootstrap,
route guards and global 401 handling. Product flows for browsing,
reservations, payment and administration arrive with issues #46 and later
(see [`docs/12-roadmap/backlog-mvp-client.md`](../../docs/12-roadmap/backlog-mvp-client.md)).

## Baseline

- Angular 22 standalone application
- Ionic 9
- Capacitor 8
- Angular Router
- Typed API client, error model and endpoint catalog (`core/api`)
- Web authentication/session state (`core/auth`)
- A reusable page shell (`layout/app-shell`)
- A neutral product landing page (`features/landing`)
- A web login page (`features/login`)
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
│   │   └── health/      # HealthService (root-level /health, not /api)
│   ├── layout/           # reusable page chrome
│   │   └── app-shell/    # header + content frame every page composes
│   ├── features/         # product screens, one folder per feature
│   │   ├── landing/       # authenticated landing page
│   │   ├── login/         # public web login
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

`core` never imports from `features`. `features/resident/` and
`features/admin/` arrive with later product slices once there is a resident
or administrator workflow to render — they are not created empty ahead of
time.

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
