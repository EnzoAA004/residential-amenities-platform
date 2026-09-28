# Client

Angular + Ionic + Capacitor client for Residential Amenities Platform.

## Status

Frontend foundation (issue #44): a real application shell, a small design
system, and a typed API client/error model/endpoint catalog. No product
flow (authentication, browsing, booking, payment, admin) is implemented
yet — those arrive with issues #45 and later
(see [`docs/12-roadmap/backlog-mvp-client.md`](../../docs/12-roadmap/backlog-mvp-client.md)).

## Baseline

- Angular 22 standalone application
- Ionic 9
- Capacitor 8
- Angular Router
- Typed API client, error model and endpoint catalog (`core/api`)
- A reusable page shell (`layout/app-shell`)
- A neutral product landing page (`features/landing`)
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
│   │   ├── config/      # API_BASE_URL token
│   │   └── health/      # HealthService (root-level /health, not /api)
│   ├── layout/           # reusable page chrome
│   │   └── app-shell/    # header + content frame every page composes
│   ├── features/         # product screens, one folder per feature
│   │   ├── landing/       # the real landing page
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
`features/admin/` arrive with issue #45 onward, once there is an
authenticated resident/admin experience to put in them — they are not
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
