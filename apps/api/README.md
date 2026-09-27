# API

ASP.NET Core backend for Residential Amenities Platform.

## Runtime baseline

- .NET 10
- ASP.NET Core Minimal APIs
- ASP.NET Core Identity
- Entity Framework Core 10
- PostgreSQL 18 through Npgsql
- Built-in ASP.NET Core OpenAPI generation

## Solution

`ResidentialAmenities.slnx` contains the API and backend test project.

```bash
dotnet tool restore
dotnet restore apps/api/ResidentialAmenities.slnx
dotnet build apps/api/ResidentialAmenities.slnx
dotnet test apps/api/ResidentialAmenities.slnx
```

## Local PostgreSQL

From repository root:

```bash
docker compose up -d postgres
```

Set the API connection string outside source control.

Preferred local approach:

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=residential_amenities;Username=residential_app;Password=local_dev_only" --project apps/api/ResidentialAmenities.Api.csproj
```

A shell environment variable also works when useful for automation:

```powershell
$env:ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=residential_amenities;Username=residential_app;Password=local_dev_only"
```

The values above are development-only defaults. Real credentials must never be committed.

## EF Core migrations

Apply migrations:

```bash
dotnet tool restore
dotnet ef database update \
  --project apps/api/ResidentialAmenities.Api.csproj \
  --context AppDbContext
```

CI checks for pending model changes, so EF model changes without a matching migration fail validation.

## Development data

In Development, startup seeds:

- one pilot building;
- timezone `America/Argentina/Buenos_Aires`;
- units `1A` through `5B`.

Base Identity roles (`Resident` and `Administrator`) are seeded by the Identity migration.

No real resident/admin account is seeded by the application.

## Authentication

Public self-registration is intentionally disabled.

### Login

```http
POST /api/auth/login?useCookies=true
```

Use `useCookies=true` for the browser/web session.

For mobile/non-browser clients:

```http
POST /api/auth/login?useCookies=false
```

The response contains ASP.NET Core Identity bearer/refresh credentials. These are opaque application tokens, not custom JWTs.

### Refresh

```http
POST /api/auth/refresh
```

### Logout

```http
POST /api/auth/logout
```

Requires an authenticated session.

### Current user

```http
GET /api/auth/me
```

Returns the authenticated user's basic profile, roles and active residential memberships.

### RBAC verification endpoints

```http
GET /api/auth/check/resident
GET /api/auth/check/admin
```

These are foundation-phase endpoints used to verify Resident/Admin policies.

## Authentication defaults

- unique email;
- minimum password length 10 with upper/lower/digit/symbol;
- 5 failed attempts before 15-minute lockout;
- web cookie: HttpOnly, SameSite=Lax, sliding eight-hour lifetime;
- bearer access token: 20 minutes;
- bearer refresh token: 14 days.

Production HTTPS, client secure storage and account onboarding/recovery are refined in the security/onboarding work.

## Run

```bash
dotnet run --project apps/api/ResidentialAmenities.Api.csproj
```

Development URLs:

- Health: `http://localhost:8080/health`
- Compatibility health: `http://localhost:8080/api/health`
- OpenAPI: `http://localhost:8080/openapi/v1.json`

OpenAPI is exposed only in Development.

## Reset local database

```bash
docker compose down -v
docker compose up -d postgres
dotnet ef database update \
  --project apps/api/ResidentialAmenities.Api.csproj \
  --context AppDbContext
```

This is destructive and intended only for local development.
