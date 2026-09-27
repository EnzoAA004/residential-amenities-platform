# Deployment Direction

This is a logical target, not yet a final Azure bill of materials.

```mermaid
flowchart TB
    GH[Private GitHub repository]
    CI[GitHub Actions]
    TF[Terraform]
    Registry[Private container registry]
    Web[Hosted web client]
    API[ASP.NET Core container]
    DB[(Managed PostgreSQL)]
    Secrets[Secret store]
    Monitor[Monitoring / logs]
    MP[Mercado Pago]

    GH --> CI
    CI -->|build/test| Registry
    CI -->|deploy application| API
    CI -->|build/deploy| Web
    CI -->|plan/apply| TF
    TF --> Web
    TF --> API
    TF --> DB
    TF --> Secrets
    TF --> Monitor
    API --> DB
    API --> Secrets
    API --> Monitor
    API --> MP
```

## Environments

Target lifecycle:

- local development;
- staging;
- production.

Environment separation is handled through configuration/IaC/deployment environments rather than long-lived Git branches.
