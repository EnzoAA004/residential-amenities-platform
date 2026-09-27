# Client

Angular + Ionic + Capacitor client for Residential Amenities Platform.

## Baseline

- Angular 22 standalone application
- Ionic 9
- Capacitor 8
- Angular Router
- Centralized API configuration
- Small API/health service layer
- Responsive home/health screen

## Install and run

From `apps/client`:

```bash
npm install
npm start
```

Open `http://localhost:8100`.

The development proxy forwards `/api/*` to the ASP.NET Core API at `http://localhost:8080`.

## Build

```bash
npm run build
```

## Client structure

```text
src/
├── app/
│   ├── core/
│   │   ├── api/
│   │   ├── config/
│   │   └── health/
│   ├── features/
│   │   └── home/
│   ├── app.component.ts
│   ├── app.config.ts
│   └── app.routes.ts
├── environments/
├── main.ts
└── styles.scss
```

The root component is only the application/router shell. Feature components do not hard-code backend URLs; HTTP calls go through the core API/service layer.

## API configuration

Development and web production currently use the same-origin `/api` base path. This works with the local development proxy and supports a future web reverse-proxy/gateway deployment.

Native mobile builds will eventually require the production API origin to be configured for that environment before release. That value must not be embedded as a secret; API origins are configuration, while credentials/tokens remain protected separately.

## Capacitor

Capacitor is initialized but native platform directories are not committed yet.

After a successful web build:

```bash
npm run cap:add:android
npm run cap:add:ios
npm run cap:sync
```

Android can be developed on supported Android tooling. iOS native builds require macOS/Xcode.

Publishing/signing is intentionally outside this Phase 1 scaffold.

## Health flow

```text
HomePage
   ↓
HealthService
   ↓
ApiClient
   ↓
/api/health
   ↓
ASP.NET Core
   ↓
PostgreSQL
```

This health UI remains a technical foundation screen and will be replaced by product flows as authentication and reservations are implemented.
