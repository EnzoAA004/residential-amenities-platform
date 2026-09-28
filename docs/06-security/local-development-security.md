# Local Development Security

This document defines the local configuration baseline before cloud deployment.

## Secret sources

Use this order of preference:

1. **.NET User Secrets** for developer-specific API secrets.
2. Environment variables for CI or temporary shell sessions.
3. `.env` only for local tooling that requires it; the real `.env` file is ignored by Git.

Never commit real credentials to:

- `appsettings*.json`;
- `.env.example`;
- source files;
- test fixtures;
- README examples;
- GitHub issue/PR bodies.

The repository's `.env.example` contains only development placeholders.

## PostgreSQL local secret

Example:

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=residential_amenities;Username=residential_app;Password=local_dev_only" --project apps/api/ResidentialAmenities.Api.csproj
```

List configured development secrets:

```bash
dotnet user-secrets list --project apps/api/ResidentialAmenities.Api.csproj
```

Do not paste the output into issues, CI logs or screenshots.

## CORS

Development origins are explicitly allowlisted in `appsettings.Development.json`.

Current local origins:

- `http://localhost:4200`
- `http://localhost:8100`

Do not replace this with `AllowAnyOrigin()` when credentials/cookies are enabled.

Production origins will be configured separately and must not inherit a wildcard.

## Logging

Application code and operational tooling must not log:

- passwords;
- Authorization headers;
- access tokens;
- refresh tokens;
- auth cookies / Set-Cookie values;
- Mercado Pago access tokens or webhook secrets;
- database connection strings;
- Azure credentials;
- complete sensitive request bodies.

Structured logs may contain safe identifiers such as:

- correlation/request ID;
- user ID;
- building ID;
- reservation ID;
- payment internal ID;
- operation name;
- success/failure category.

Sensitive-data logging in EF Core must remain disabled outside tightly controlled local debugging.

## Mercado Pago secrets

Treat these as secrets:

- private access token — `MercadoPago:AccessToken`;
- webhook verification secret — `MercadoPago:WebhookSecret`;
- any private application credential returned by Mercado Pago.

Only the configuration **names** are in source control; the checked-in
`appsettings*.json` never contain values. Set your own (test) credentials
locally with User Secrets, or with environment variables
(`MercadoPago__AccessToken`, `MercadoPago__WebhookSecret`):

```bash
dotnet user-secrets set "MercadoPago:AccessToken" "<your test access token>" --project apps/api/ResidentialAmenities.Api.csproj
dotnet user-secrets set "MercadoPago:WebhookSecret" "<your webhook secret>" --project apps/api/ResidentialAmenities.Api.csproj
```

Non-secret settings (`MercadoPago:ApiBaseUrl`, `SuccessUrl`, `PendingUrl`,
`FailureUrl`, `WebhookToleranceSeconds`, `RequestTimeoutSeconds`) may live in
configuration. Tests and CI use a fake provider and need no credentials.

Additionally never log: the full `x-signature`, the `Authorization` header,
or unnecessary payer personal data. The webhook logs only the failure
category of a rejected signature. `PaymentProviderEvents` stores identifiers,
never payloads, payer data or signatures.

Public identifiers may be configuration, but must still be documented separately from secrets.

## Future Azure secrets

Production should prefer Azure-managed identity/service-to-service authentication where supported.

Secrets that cannot be removed should move to the selected managed secret store (expected direction: Azure Key Vault) and be referenced by the runtime rather than copied into source control or Terraform variables committed to Git.

Terraform state itself is sensitive and must never be committed to this repository.

## Pull Request checklist

Before merging a change that introduces configuration:

- [ ] no real secret is present in the diff;
- [ ] example values are obviously non-production;
- [ ] new secret keys are documented by name, not value;
- [ ] logs do not expose the new secret;
- [ ] CORS changes remain explicit;
- [ ] production secret storage impact is noted when applicable.

## Audit trail

The `AuditLogs` table stores business facts only. It must never receive
passwords, tokens, cookies, secrets, `Authorization` headers, `x-signature`,
connection strings, card data, full request bodies or the e-mail typed at
login. Metadata is produced by the typed helpers in `AuditMetadata`; if a new
fact needs a new field, add it to that allowlist deliberately rather than
serializing an object.
