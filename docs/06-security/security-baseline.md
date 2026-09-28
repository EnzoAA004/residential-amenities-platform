# Security Baseline

## Trust principles

- The client is not trusted to calculate authoritative price, availability, role or payment status.
- All privileged authorization is enforced by the backend.
- Private provider credentials and database credentials remain server-side.
- Repository and cloud administration remain private.

## Authentication / session

The authentication foundation uses **ASP.NET Core Identity** with the application database as the Identity store.

Two first-party client modes are supported:

### Web client

- Cookie-based Identity session.
- Cookie is `HttpOnly`.
- Development over local HTTP uses `residential-auth` with `SameAsRequest` secure policy.
- Non-Development environments use `__Host-residential-auth` with `Secure=Always`, `Path=/` and no `Domain`.
- `SameSite=Lax`.
- Sliding session with an eight-hour application-cookie lifetime.
- Security-stamp validation interval: five minutes.
- Cross-origin credential support is restricted to explicitly configured CORS origins.

### Mobile / non-browser client

- ASP.NET Core Identity opaque bearer access token.
- Access-token lifetime: 20 minutes.
- Refresh-token lifetime: 14 days.
- The application does not implement custom JWT signing or custom cryptography.
- Bearer/refresh tokens are credentials and must never be logged.
- Client secure-storage integration is part of the mobile authentication UI work; tokens must not be stored in ordinary web localStorage.

## Account creation

There is intentionally **no public registration endpoint**.

A resident must not be able to create an account and assign themselves to an arbitrary unit. User creation/invitation/onboarding will be an administrator-controlled application workflow once the stakeholder onboarding rule in issue #2 is finalized.

## Password / brute-force baseline

Identity currently enforces:

- minimum length: 10;
- uppercase required;
- lowercase required;
- digit required;
- non-alphanumeric required;
- lockout enabled for new users;
- lockout after 5 failed attempts;
- lockout period: 15 minutes.

Password hashes are produced and managed by ASP.NET Core Identity. Plaintext passwords are never persisted by application code.

## Authorization

Initial roles:

- `Resident`
- `Administrator`

Policies:

- `ResidentAccess`: Resident or Administrator.
- `Administrator`: Administrator only.

The backend derives roles from the Identity store/claims principal. Role names supplied in client request bodies are never authoritative.

Building/unit authorization remains a separate domain concern through active `ResidentMembership` records. Authentication answers **who the user is**; membership answers **which building/unit context the user may operate in**.

## Identity endpoints

Current controlled endpoints:

- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `GET /api/auth/me`
- `GET /api/auth/check/resident` — verification endpoint
- `GET /api/auth/check/admin` — verification endpoint

The check endpoints exist to prove policy enforcement during the foundation phase and can be removed/replaced as real protected use cases appear.

## Payments

- Never store card data.
- Keep Mercado Pago private credentials server-side.
- Treat provider callbacks/webhooks as untrusted input until verified.
- Make payment-event processing idempotent.
- Do not confirm a booking solely from a client redirect.

Implemented in issue #24:

- `POST /api/webhooks/mercadopago` is anonymous by necessity but every call
  must carry a valid `x-signature` (HMAC-SHA256 over
  `id:<data.id>;request-id:<x-request-id>;ts:<ts>;`, constant-time compare,
  timestamp tolerance). Invalid → 401, nothing stored, nothing fetched.
- The signature does not cover the body, so the order id comes only from the
  signed `data.id` query; the order is then re-fetched with the private token
  and its id, `external_reference`, amount and currency are validated against
  the local payment.
- Amount and currency come from the reservation's price snapshot, never from
  the client.
- Idempotency: persisted `X-Idempotency-Key`, unique provider order/event ids,
  state-based reconciliation.
- Approval after the hold expired never revives the reservation; it is
  flagged for manual review.
- Never logged/stored: access token, webhook secret, full `x-signature`,
  `Authorization`, payer data.

## Data

- Minimize personal data.
- Do not place secrets, passwords, access tokens, refresh tokens or connection strings in logs.
- Encrypt traffic in transit.
- Use managed secret storage for production credentials.
- Define backup/restore and retention before production.

## Local configuration baseline

Developer-specific API secrets use .NET User Secrets or environment variables and never live in source-controlled configuration. Development CORS remains an explicit allowlist.

See [Local Development Security](local-development-security.md) for secret names, logging rules and the future Mercado Pago/Azure checklist.

## Application security backlog

Still to be addressed/refined in later issues:

- threat model;
- production HTTPS/HSTS configuration;
- rate limiting beyond Identity lockout;
- security headers;
- client token secure-storage implementation;
- account invitation/recovery workflow;
- CORS production allowlist;
- dependency scanning;
- container scanning;
- audit-event policy;
- incident response procedure.

Cash (issue #25): declaring cash is limited to the reservation's creator (or
an Administrator); confirming receipt is `Administrator`-only — provisional
until OQ-013 defines who receives cash — with the actor always taken from the
authenticated session and stored on the payment (`CashConfirmedByUserId`,
`CashConfirmedAtUtc`). The resident-facing payment view never exposes that id.
