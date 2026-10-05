# Deployment Direction

The target Azure topology is now implemented in Terraform. The diagram below
describes the intended staging/pilot runtime. It is **not yet evidence of a live
Azure deployment** until the gated staging workflow is executed successfully.

```mermaid
flowchart TB
    GH[GitHub repository]
    CI[GitHub Actions]
    TF[Terraform]
    ACR[Azure Container Registry]
    Web[Azure Container App · Web / Nginx]
    API[Azure Container App · ASP.NET Core API]
    PG[(Private PostgreSQL Flexible Server)]
    Blob[(Private Azure Blob Storage)]
    ID[User-assigned Managed Identity]
    Logs[Log Analytics]
    MP[Mercado Pago]

    GH --> CI
    CI -->|validate IaC / build| TF
    CI -->|ACR Build| ACR
    TF --> ACR
    TF --> Web
    TF --> API
    TF --> PG
    TF --> Blob
    TF --> ID
    TF --> Logs

    ACR --> Web
    ACR --> API
    Web -->|same-origin /api proxy| API
    API --> PG
    API --> Blob
    ID --> ACR
    ID --> Blob
    API --> Logs
    API --> MP
```

## Networking

- Container Apps runs inside a dedicated VNet subnet.
- PostgreSQL Flexible Server uses a delegated subnet with private DNS and
  public network access disabled.
- The API Container App uses internal ingress only.
- The web Container App is public and reverse-proxies `/api/*` to the
  internal API, keeping browser traffic same-origin.

## Durable application state

- relational state: PostgreSQL Flexible Server;
- uploaded media: private Azure Blob container via managed identity;
- ASP.NET Core Data Protection key ring: private Blob container via managed identity;
- application images: ACR;
- logs: Log Analytics.

## Environments

Target lifecycle:

- local development;
- staging;
- production.

Environment separation is handled through configuration/IaC/deployment
environments rather than long-lived Git branches.
