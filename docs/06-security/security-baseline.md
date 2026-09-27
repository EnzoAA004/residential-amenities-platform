# Security Baseline

## Trust principles

- The client is not trusted to calculate authoritative price, availability, role or payment status.
- All privileged authorization is enforced by the backend.
- Private provider credentials and database credentials remain server-side.
- Repository and cloud administration remain private.

## Authentication / session

Final implementation details require a dedicated spike. The design must support secure web and mobile session handling without exposing long-lived secrets in insecure client storage.

## Authorization

Initial roles:
- Resident
- Administrator

Additional roles such as a cash-payment confirmer should only be introduced when the business process requires them.

## Payments

- Never store card data.
- Keep Mercado Pago private credentials server-side.
- Treat provider callbacks/webhooks as untrusted input until verified.
- Make payment-event processing idempotent.
- Do not confirm a booking solely from a client redirect.

## Data

- Minimize personal data.
- Do not place secrets in logs.
- Encrypt traffic in transit.
- Use managed secret storage for production credentials.
- Define backup/restore and retention before production.

## Application security backlog

- threat model;
- rate limiting;
- account lockout/brute-force protection;
- validation strategy;
- CORS policy;
- security headers;
- dependency scanning;
- container scanning;
- audit-event policy;
- incident response procedure.
