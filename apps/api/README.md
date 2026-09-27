# API

ASP.NET Core backend.

## Toolchain spike

The current spike exposes:

- `GET /api/health`
- PostgreSQL connectivity through Npgsql

Run locally:

```powershell
$env:ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=residential_amenities;Username=residential_app;Password=local_dev_only"
dotnet run --project apps/api/ResidentialAmenities.Api.csproj
```

The permanent modular backend scaffold will be refined in issue #6 after this spike is validated.
