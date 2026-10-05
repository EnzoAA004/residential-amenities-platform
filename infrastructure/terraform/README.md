# Terraform — Azure Phase 7

This directory contains the first deployable Azure IaC baseline for the Residential Amenities Platform.

## What the stack provisions

- pre-created dedicated staging resource group plus Terraform-managed VNet; resource deployment location comes from the explicit Terraform `location` variable rather than resource-group metadata;
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

## Azure account + OIDC bootstrap

For Windows/PowerShell, the preferred bootstrap automates the first account-side setup:

```powershell
.\scripts\bootstrap-azure-staging.ps1
```

It verifies Azure CLI and GitHub CLI authentication, registers required Azure providers, creates/reuses the staging application resource group plus the small Terraform remote-state Storage account, creates/reuses a user-assigned managed identity, configures a federated OIDC credential on that identity for the GitHub `staging` environment, assigns the required roles, and writes the GitHub Environment secrets/variables through `gh`. This avoids requiring tenant-level application-registration permission. The bootstrap derives GitHub's actual immutable OIDC subject from repository owner/repository IDs so the Azure federated credential matches the token emitted by GitHub Actions.

The script first checks the subscription's `Allowed resource deployment regions` policy and fails before resource creation if the requested region is not permitted. The current Azure for Students staging default is `canadacentral`.

The script requires typing `BOOTSTRAP` before creating Azure-side bootstrap resources. The staging resource group itself has no direct charge, while the remote-state Storage account can incur a small charge. It does **not** run `terraform apply`; the optional `-RunPlan` switch only dispatches a Terraform plan.

The GitHub deployment user-assigned managed identity is deliberately scoped to the staging application resource group instead of the whole subscription. Azure Resource Provider registration is therefore performed by the local account bootstrap, while the Terraform AzureRM provider explicitly disables automatic registration (`resource_provider_registrations = "none"`). It receives `Contributor` plus `Role Based Access Control Administrator` only on that resource group so Terraform can create resources and the two managed-identity role assignments declared by the stack. Terraform state access is granted separately with `Storage Blob Data Contributor` on the remote-state Storage account.

Linux/macOS users can still bootstrap only remote state with:

```bash
./scripts/bootstrap-azure-state.sh \
  rg-resamen-tfstate \
  stresamentfstateUNIQUE \
  canadacentral
```

For local Terraform usage, `backend.hcl.example` remains available as a template.

## Real staging deployment

Use GitHub Actions workflow **Deploy Azure staging** after running the OIDC bootstrap (or configuring the same values manually). The workflow supports plan-only and apply modes. Apply mode additionally builds the API/client images in ACR, updates both Container Apps and performs a smoke check through the public web endpoint.

Required GitHub Environment secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Required GitHub Environment variables:

- `TFSTATE_RESOURCE_GROUP`
- `TFSTATE_STORAGE_ACCOUNT`
- `TFSTATE_CONTAINER` (normally `tfstate`)
- `AZURE_LOCATION` (for example `canadacentral`)

Mercado Pago credentials are intentionally not stored in Terraform state. Add them as deployment secrets before testing real provider payments.

## Cost control

This is designed for a small pilot/staging footprint:

- Container Apps use the Consumption plan and can scale to zero.
- PostgreSQL defaults to Burstable `B_Standard_B1ms`.
- ACR defaults to Basic.
- Log retention is 30 days.
- PostgreSQL geo-redundant backup is disabled for staging.

Before any real `apply`, review Azure Pricing and create an Azure budget/alert. The manual workflow requires explicit acknowledgement that Azure resources can incur charges.
