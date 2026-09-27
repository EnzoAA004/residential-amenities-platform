# Local Toolchain Spike

Issue: #4

## Goal

Prove the selected stack can work together before committing to the full application scaffold.

## Version baseline

Checked on 2026-09-27:

| Technology | Baseline |
| --- | --- |
| .NET SDK | 10.0.401 / .NET 10 LTS |
| ASP.NET Core | .NET 10 |
| Node.js | 24 LTS |
| Angular | 22.2 |
| Ionic Framework | 9.0 |
| Capacitor | 8.5 |
| PostgreSQL | 18 |
| Npgsql | 10.0.3 |

Patch/minor updates may move during development when compatible. Major upgrades require explicit review.

## Spike topology

```text
Angular / Ionic
      |
      | GET /api/health
      v
ASP.NET Core
      |
      | SELECT 1
      v
PostgreSQL
```

## Local prerequisites

- .NET 10 SDK
- Node.js 24 LTS
- npm
- Docker Desktop
- Git

Verify:

```bash
dotnet --info
node -v
npm -v
docker --version
git --version
```

## Start PostgreSQL

From repository root:

```bash
docker compose up -d postgres
```

Defaults in `docker-compose.yml` are local-development-only values. Copy `.env.example` to `.env` if you want to override them locally. Never commit real credentials.

## Start the API

PowerShell example:

```powershell
$env:ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=residential_amenities;Username=residential_app;Password=local_dev_only"
dotnet run --project apps/api/ResidentialAmenities.Api.csproj
```

Verify:

```text
http://localhost:8080/api/health
```

Expected shape:

```json
{
  "status": "ok",
  "database": "ok",
  "utc": "..."
}
```

## Start the client

```bash
cd apps/client
npm install
npm start
```

Open `http://localhost:8100`.

The Angular development proxy forwards `/api/*` requests to the ASP.NET API on port 8080.

## Capacitor

Capacitor is initialized through `capacitor.config.ts`, but native Android/iOS projects are intentionally not added in this spike. Native packaging belongs to the permanent client scaffold.

## Automated validation

The checks this spike proved out now run as the permanent `CI` GitHub Actions workflow
(`.github/workflows/ci.yml`). See [ci.md](ci.md) for the current pipeline and how to
reproduce it locally.

## Exit criteria

- API builds on .NET 10.
- PostgreSQL starts reproducibly.
- API can execute a database round-trip.
- Angular/Ionic client builds on Node 24.
- Client contains a real call to the API health endpoint.
- No Azure/cloud credentials are required.
