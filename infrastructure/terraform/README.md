# Terraform — Azure Phase 7

This directory contains the first deployable Azure IaC baseline for the Residential Amenities Platform.

## What the stack provisions

- dedicated resource group and VNet;
- Azure Container Apps environment on a delegated subnet;
- private Azure Database for PostgreSQL Flexible Server;
- Azure Container Registry (Basic);
- private Blob containers for media and ASP.NET Core Data Protection keys;
- user-assigned managed identity with `AcrPull` and `Storage Blob Data Contributor`;
- internal API Container App;
- public web Container App that reverse-proxies `/api/*` to the internal API;
- Log Analytics workspace.

The application containers scale to zero by default. PostgreSQL and ACR are still billable Azure resources even when the apps are idle.

## Security choices

- PostgreSQL has public network access disabled and uses a delegated subnet/private DNS.
- Blob containers are private.
- Application access to ACR and Blob Storage uses managed identity.
- No Azure storage keys or registry admin credentials are placed in application config.
- ASP.NET Core Data Protection keys are persisted to Blob Storage so auth state survives container restarts.
- The web client keeps `/api` same-origin through Nginx instead of exposing the API publicly.

## Important scope boundary

This code is **implemented IaC**, but it is not proof that Azure resources are currently running. Nothing in CI automatically executes `terraform apply`. The manual deployment workflow requires Azure OIDC credentials and an explicit cost acknowledgement.

## Validate locally without Azure credentials

```bash
cd infrastructure/terraform
terraform fmt -check -recursive
terraform init -backend=false
terraform validate
```

## Remote state bootstrap

Create the state resources once before the first real plan/apply. The helper script is intentionally manual:

```bash
./scripts/bootstrap-azure-state.sh \
  rg-resamen-tfstate \
  stresamentfstateUNIQUE \
  brazilsouth
```

Then use `backend.hcl.example` as the template for your local backend configuration:

```bash
terraform init -backend-config=backend.hcl
```

## Real staging deployment

Use GitHub Actions workflow **Deploy Azure staging** after configuring the `staging` GitHub Environment with OIDC credentials and backend variables. The workflow supports plan-only and apply modes. Apply mode additionally builds the API/client images in ACR, updates both Container Apps and performs a smoke check through the public web endpoint.

Required GitHub Environment secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Required GitHub Environment variables:

- `TFSTATE_RESOURCE_GROUP`
- `TFSTATE_STORAGE_ACCOUNT`
- `TFSTATE_CONTAINER` (normally `tfstate`)
- `AZURE_LOCATION` (for example `brazilsouth`)

Mercado Pago credentials are intentionally not stored in Terraform state. Add them as deployment secrets before testing real provider payments.

## Cost control

This is designed for a small pilot/staging footprint:

- Container Apps use the Consumption plan and can scale to zero.
- PostgreSQL defaults to Burstable `B_Standard_B1ms`.
- ACR defaults to Basic.
- Log retention is 30 days.
- PostgreSQL geo-redundant backup is disabled for staging.

Before any real `apply`, review Azure Pricing and create an Azure budget/alert. The manual workflow requires explicit acknowledgement that Azure resources can incur charges.
