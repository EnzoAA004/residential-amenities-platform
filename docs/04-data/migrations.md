# Database Migrations

## Baseline

The application uses EF Core migrations against PostgreSQL.

Versions selected on 2026-09-27:

- EF Core: 10.0.12
- Npgsql EF Core provider: 10.0.3
- dotnet-ef: 10.0.12
- PostgreSQL: 18

## Initial migration

`20260927163000_InitialInfrastructure` is intentionally schema-empty.

It proves and versions the migration mechanism before domain entities are introduced. Applying it creates EF Core's migrations history and establishes a known baseline.

The first domain tables will be introduced by issue #9.

## Rules

1. Schema changes are made through migrations, not ad-hoc production SQL.
2. Migration files are committed with the code that requires them.
3. Existing applied migrations are not rewritten casually; corrective changes get a new migration.
4. CI must be able to apply all migrations from an empty PostgreSQL database.
5. Connection strings come from external configuration.
6. Production migrations will eventually run through a controlled deployment step rather than each app replica racing to migrate on startup.

## Local reset

For local development only:

```bash
docker compose down -v
docker compose up -d postgres
```

Then export `ConnectionStrings__Postgres` and run:

```bash
dotnet tool restore
dotnet ef database update \
  --project apps/api/ResidentialAmenities.Api.csproj \
  --context AppDbContext
```

## PostgreSQL 18 Docker note

The official PostgreSQL image changed its persistent volume root in version 18. The repository therefore mounts the named volume at:

```text
/var/lib/postgresql
```

not the pre-18 path `/var/lib/postgresql/data`.
