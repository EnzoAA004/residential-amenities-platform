# API

ASP.NET Core backend for Residential Amenities Platform.

## Runtime baseline

- .NET 10
- ASP.NET Core Minimal APIs
- PostgreSQL via Npgsql
- Built-in ASP.NET Core OpenAPI generation

## Solution

`ResidentialAmenities.slnx` is the backend solution container. .NET 10 uses the XML-based SLNX solution format by default.

Build:

```bash
dotnet restore apps/api/ResidentialAmenities.slnx
dotnet build apps/api/ResidentialAmenities.slnx
```

## Configuration

The API requires the PostgreSQL connection string from configuration. It is intentionally not committed as a real secret.

PowerShell:

```powershell
$env:ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=residential_amenities;Username=residential_app;Password=local_dev_only"
```

Development CORS origins live in `appsettings.Development.json`. Production origins will be supplied through environment-specific configuration.

## Run

```bash
dotnet run --project apps/api/ResidentialAmenities.Api.csproj
```

Development URLs with the committed launch profile:

- Health: `http://localhost:8080/health`
- Compatibility health route: `http://localhost:8080/api/health`
- OpenAPI JSON: `http://localhost:8080/openapi/v1.json`

OpenAPI is exposed only in the Development environment.

## Structure

```text
apps/api/
├── Endpoints/
│   └── HealthEndpoints.cs
├── Infrastructure/
│   └── Errors/
│       └── GlobalExceptionHandler.cs
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

Each module currently contains only a registration boundary. Domain behavior is intentionally deferred to its dedicated issues.

## Error handling

Unhandled exceptions pass through a central `IExceptionHandler` implementation that returns a generic Problem Details payload with a trace ID. Internal exception details are logged server-side rather than returned to clients.

## Architectural rule

`Program.cs` is the composition root. Modules must not access another module's persistence implementation directly. See `docs/03-architecture/module-boundaries.md`.
