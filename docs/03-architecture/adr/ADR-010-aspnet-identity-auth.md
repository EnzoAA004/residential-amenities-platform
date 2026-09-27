# ADR-010 — Use ASP.NET Core Identity for Authentication and RBAC

- **Status:** Accepted
- **Date:** 2026-09-27

## Context

The platform needs authentication for both browser and mobile clients, server-side role enforcement, account lockout/password hashing, refreshable mobile credentials and a future path for account recovery/external login.

The application must also prevent public self-registration because building/unit membership is controlled by the residential domain.

## Options considered

- Custom JWT authentication and custom password persistence.
- External identity provider as a mandatory dependency from MVP.
- ASP.NET Core Identity with cookie and built-in bearer authentication.

## Decision

Use **ASP.NET Core Identity** backed by PostgreSQL.

Client modes:

- web: Identity application cookie;
- mobile/non-browser: ASP.NET Core Identity opaque bearer access token plus refresh token.

Do not expose a public registration endpoint.

Initial roles:

- Resident;
- Administrator.

Residential authorization remains separate from role assignment through `ResidentMembership`.

## Rationale

- Avoid implementing password hashing/token cryptography ourselves.
- Use framework lockout, security stamps, roles and EF stores.
- Support browser and mobile clients without forcing a custom JWT implementation.
- Keep building/unit membership under explicit application-controlled onboarding rather than Identity registration.
- Preserve a clean separation between "who is the user?", "what application role do they have?" and "which building/unit may they operate in?".

## Consequences

### Positive

- Password hashing and credential lifecycle use maintained framework primitives.
- RBAC is enforced server-side.
- Web cookies can remain HttpOnly.
- Mobile clients can use bearer/refresh credentials without sharing cookie behavior.
- Future external-login/recovery features can build on Identity stores.

### Trade-offs

- Identity introduces additional persistence tables and columns.
- Opaque bearer tokens are application-specific and are not intended as general third-party OAuth/OIDC access tokens.
- Client secure-storage behavior still needs implementation.
- Account invitation/onboarding remains a separate application workflow.

## Security constraints

- Never log passwords, access tokens or refresh tokens.
- Never trust client-supplied role names.
- No public `/register` route.
- Production traffic must use HTTPS.
- Administrator-controlled onboarding must validate residential membership.
