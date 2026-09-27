# Continuous Integration

Issue: #12

## Purpose

Prevent broken backend or frontend changes from reaching `main`. This consolidates and
supersedes the exploratory `Toolchain Spike` workflow (see
[toolchain-spike.md](toolchain-spike.md) for the original spike record) now that the
checks it proved out are the project's actual Definition of Done.

## Workflow

File: `.github/workflows/ci.yml` — job `validate-stack`.

Triggers on every pull request targeting `main`, and manually via `workflow_dispatch`.

Steps:

1. Checkout.
2. Setup .NET 10 SDK.
3. Restore local .NET tools (`dotnet-ef`).
4. Restore and build the API solution (`Release`).
5. Validate EF Core migrations against a disposable PostgreSQL 18 service container:
   - list applied migrations,
   - fail if the model has pending changes not captured by a migration,
   - apply migrations to the CI database.
6. Run backend tests (`dotnet test`).
7. Start the API and verify `/health` and `/openapi/v1.json` respond.
8. Setup Node 24 LTS with npm cache keyed on `apps/client/package-lock.json`.
9. Install client dependencies reproducibly (`npm ci`).
10. Run client tests (`npm test`, Vitest via Angular CLI, headless).
11. Build the Angular/Ionic client for production (`npm run build`).

No Azure or other cloud credentials are used or required.

## Reproducing CI locally

Backend:

```bash
docker compose up -d postgres
dotnet tool restore
dotnet restore apps/api/ResidentialAmenities.slnx
dotnet build apps/api/ResidentialAmenities.slnx --configuration Release
dotnet ef database update --project apps/api/ResidentialAmenities.Api.csproj --context AppDbContext
dotnet test apps/api/ResidentialAmenities.slnx --configuration Release
```

Frontend:

```bash
cd apps/client
npm ci
npm test
npm run build
```

## Notes

- `apps/client/package-lock.json` is committed so both `npm ci` locally and in CI resolve
  the exact same dependency tree.
- Migration names are asserted explicitly in the workflow so a missing/renamed migration
  fails CI instead of silently skipping validation; update the list when adding migrations.
- Dependency caching is currently limited to npm (via `actions/setup-node`'s built-in
  cache). NuGet caching was intentionally left out because the API project does not use a
  `packages.lock.json`; revisit if restore time becomes a problem.
