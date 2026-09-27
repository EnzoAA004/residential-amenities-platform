# API

ASP.NET Core backend for Residential Amenities Platform.

## Runtime baseline

- .NET 10
- ASP.NET Core Minimal APIs
- Entity Framework Core 10
- PostgreSQL 18 through the Npgsql EF Core provider
- Built-in ASP.NET Core OpenAPI generation

## Solution

`ResidentialAmenities.slnx` is the backend solution container.

Build:

```bash
dotnet tool restore
dotnet restore apps/api/ResidentialAmenities.slnx
dotnet build apps/api/ResidentialAmenities.slnx
```

## Local PostgreSQL

From the repository root:

```bash
docker compose up -d postgres
```

PostgreSQL 18 stores its Docker volume under `/var/lib/postgresql`; the compose file uses that location.

Set the API connection string externally.

PowerShell:

```powershell
$env:ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=residential_amenities;Username=residential_app;Password=local_dev_only"
```

The values above are development-only defaults. Production credentials must not be committed.

## EF Core migrations

The repository pins `dotnet-ef` through `.config/dotnet-tools.json`.

List migrations:

```bash
dotnet ef migrations list \
  --project apps/api/ResidentialAmenities.Api.csproj \
  --context AppDbContext
```

Apply migrations:

```bash
dotnet ef database update \
  --project apps/api/ResidentialAmenities.Api.csproj \
  --context AppDbContext
```

Create a future migration:

```bash
dotnet ef migrations add MigrationName \
  --project apps/api/ResidentialAmenities.Api.csproj \
  --context AppDbContext \
  --output-dir Infrastructure/Persistence/Migrations
```

## Reset local database

To completely delete the local database volume and recreate it:

```bash
docker compose down -v
docker compose up -d postgres
dotnet ef database update \
  --project apps/api/ResidentialAmenities.Api.csproj \
  --context AppDbContext
```

This is destructive and is only intended for local development.

## Run

```bash
dotnet run --project apps/api/ResidentialAmenities.Api.csproj
```

Development URLs:

- Health: `http://localhost:8080/health`
- Compatibility health route: `http://localhost:8080/api/health`
- OpenAPI JSON: `http://localhost:8080/openapi/v1.json`

OpenAPI is exposed only in Development.

## Structure

```text
apps/api/
├── Endpoints/
├── Infrastructure/
│   ├── Errors/
│   └── Persistence/
│       └── Migrations/
├── Modules/
│   ├── Administration/
│   ├── Amenities/
│   ├── Audit/
│   ├── Buildings/
│   ├── Identity/
│   ├── Messaging/
│   ├── Notifications/
│   ├── Payments/
│   ├── Pricing/
│   ├── Reporting/
│   └── Reservations/
├── Program.cs
├── ResidentialAmenities.Api.csproj
└── ResidentialAmenities.slnx
```

Domain tables are intentionally not introduced until issue #9.
