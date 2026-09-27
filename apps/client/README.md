# Client

Angular + Ionic + Capacitor client.

## Toolchain spike

Install and run:

```bash
npm install
npm start
```

Open `http://localhost:8100`.

The development proxy forwards `/api/*` to the ASP.NET Core API at `http://localhost:8080`.

The spike UI performs a real health request and shows API/database state. Native Android/iOS projects are intentionally deferred to issue #7.
