[CmdletBinding()]
param(
    [string]$Repository = "EnzoAA004/residential-amenities-platform",
    [string]$Environment = "staging",
    [string]$Location = "brazilsouth",
    [string]$StateResourceGroup = "rg-resamen-tfstate",
    [string]$StateContainer = "tfstate",
    [string]$ApplicationName = "resamen-github-staging",
    [switch]$RunPlan
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Command {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "'$Name' is required but was not found in PATH."
    }
}

function Invoke-AzText {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & az @Arguments 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host $output
        throw "Azure CLI command failed."
    }

    return ($output | Out-String).Trim()
}

function Invoke-Gh {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & gh @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI command failed."
    }
}

Assert-Command "az"
Assert-Command "gh"

Write-Host ""
Write-Host "Residential Amenities - Azure staging bootstrap" -ForegroundColor Cyan
Write-Host "Repository : $Repository"
Write-Host "Environment: $Environment"
Write-Host "Location   : $Location"
Write-Host ""
Write-Host "This bootstrap creates a small Azure Storage account for Terraform remote state,"
Write-Host "an Entra application/service principal and role assignments. Azure resources can"
Write-Host "incur charges. It does NOT run terraform apply. The optional -RunPlan switch"
Write-Host "only dispatches a Terraform PLAN."
Write-Host ""

$confirmation = Read-Host "Type BOOTSTRAP to continue"
if ($confirmation -ne "BOOTSTRAP") {
    throw "Cancelled."
}

Write-Host ""
Write-Host "Checking Azure login..." -ForegroundColor Yellow
$subscriptionId = Invoke-AzText @("account", "show", "--query", "id", "-o", "tsv")
$tenantId = Invoke-AzText @("account", "show", "--query", "tenantId", "-o", "tsv")
$subscriptionName = Invoke-AzText @("account", "show", "--query", "name", "-o", "tsv")

if ([string]::IsNullOrWhiteSpace($subscriptionId)) {
    throw "No active Azure subscription. Run 'az login' and select a subscription first."
}

Write-Host "Azure subscription: $subscriptionName ($subscriptionId)" -ForegroundColor Green

Write-Host ""
Write-Host "Checking GitHub login..." -ForegroundColor Yellow
& gh auth status
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI is not authenticated. Run 'gh auth login' first."
}

Write-Host ""
Write-Host "Registering required Azure resource providers..." -ForegroundColor Yellow
$providers = @(
    "Microsoft.App",
    "Microsoft.OperationalInsights",
    "Microsoft.DBforPostgreSQL",
    "Microsoft.ContainerRegistry",
    "Microsoft.Storage",
    "Microsoft.ManagedIdentity",
    "Microsoft.Network"
)

foreach ($provider in $providers) {
    Invoke-AzText @("provider", "register", "--namespace", $provider, "--wait") | Out-Null
    Write-Host "  requested: $provider"
}

Write-Host ""
Write-Host "Creating/confirming Terraform remote-state resources..." -ForegroundColor Yellow
Invoke-AzText @(
    "group", "create",
    "--name", $StateResourceGroup,
    "--location", $Location,
    "--output", "none"
) | Out-Null

$existingStorage = Invoke-AzText @(
    "storage", "account", "list",
    "--resource-group", $StateResourceGroup,
    "--query", "[?starts_with(name, 'stresamenstg')].name | [0]",
    "-o", "tsv"
)

if ([string]::IsNullOrWhiteSpace($existingStorage)) {
    $suffix = -join ((97..122) | Get-Random -Count 6 | ForEach-Object { [char]$_ })
    $stateStorageAccount = "stresamenstg$suffix"

    Invoke-AzText @(
        "storage", "account", "create",
        "--name", $stateStorageAccount,
        "--resource-group", $StateResourceGroup,
        "--location", $Location,
        "--sku", "Standard_LRS",
        "--kind", "StorageV2",
        "--min-tls-version", "TLS1_2",
        "--allow-blob-public-access", "false",
        "--output", "none"
    ) | Out-Null
}
else {
    $stateStorageAccount = $existingStorage
}

$stateStorageId = Invoke-AzText @(
    "storage", "account", "show",
    "--name", $stateStorageAccount,
    "--resource-group", $StateResourceGroup,
    "--query", "id",
    "-o", "tsv"
)

$currentUserObjectId = Invoke-AzText @("ad", "signed-in-user", "show", "--query", "id", "-o", "tsv")

$existingUserBlobRole = Invoke-AzText @(
    "role", "assignment", "list",
    "--assignee-object-id", $currentUserObjectId,
    "--scope", $stateStorageId,
    "--role", "Storage Blob Data Contributor",
    "--query", "[0].id",
    "-o", "tsv"
)

if ([string]::IsNullOrWhiteSpace($existingUserBlobRole)) {
    Invoke-AzText @(
        "role", "assignment", "create",
        "--assignee-object-id", $currentUserObjectId,
        "--assignee-principal-type", "User",
        "--role", "Storage Blob Data Contributor",
        "--scope", $stateStorageId,
        "--output", "none"
    ) | Out-Null

    Write-Host "Waiting briefly for Azure RBAC propagation..."
    Start-Sleep -Seconds 20
}

Invoke-AzText @(
    "storage", "container", "create",
    "--name", $StateContainer,
    "--account-name", $stateStorageAccount,
    "--auth-mode", "login",
    "--output", "none"
) | Out-Null

Write-Host "Terraform state: $StateResourceGroup / $stateStorageAccount / $StateContainer" -ForegroundColor Green

Write-Host ""
Write-Host "Creating/confirming Microsoft Entra OIDC application..." -ForegroundColor Yellow
$clientId = Invoke-AzText @(
    "ad", "app", "list",
    "--display-name", $ApplicationName,
    "--query", "[0].appId",
    "-o", "tsv"
)

if ([string]::IsNullOrWhiteSpace($clientId)) {
    $clientId = Invoke-AzText @(
        "ad", "app", "create",
        "--display-name", $ApplicationName,
        "--query", "appId",
        "-o", "tsv"
    )
}

$applicationObjectId = Invoke-AzText @(
    "ad", "app", "show",
    "--id", $clientId,
    "--query", "id",
    "-o", "tsv"
)

$servicePrincipalObjectId = Invoke-AzText @(
    "ad", "sp", "list",
    "--filter", "appId eq '$clientId'",
    "--query", "[0].id",
    "-o", "tsv"
)

if ([string]::IsNullOrWhiteSpace($servicePrincipalObjectId)) {
    $servicePrincipalObjectId = Invoke-AzText @(
        "ad", "sp", "create",
        "--id", $clientId,
        "--query", "id",
        "-o", "tsv"
    )
}

$subscriptionScope = "/subscriptions/$subscriptionId"

$existingContributor = Invoke-AzText @(
    "role", "assignment", "list",
    "--assignee-object-id", $servicePrincipalObjectId,
    "--scope", $subscriptionScope,
    "--role", "Contributor",
    "--query", "[0].id",
    "-o", "tsv"
)

if ([string]::IsNullOrWhiteSpace($existingContributor)) {
    Invoke-AzText @(
        "role", "assignment", "create",
        "--assignee-object-id", $servicePrincipalObjectId,
        "--assignee-principal-type", "ServicePrincipal",
        "--role", "Contributor",
        "--scope", $subscriptionScope,
        "--output", "none"
    ) | Out-Null
}

$existingStateBlobRole = Invoke-AzText @(
    "role", "assignment", "list",
    "--assignee-object-id", $servicePrincipalObjectId,
    "--scope", $stateStorageId,
    "--role", "Storage Blob Data Contributor",
    "--query", "[0].id",
    "-o", "tsv"
)

if ([string]::IsNullOrWhiteSpace($existingStateBlobRole)) {
    Invoke-AzText @(
        "role", "assignment", "create",
        "--assignee-object-id", $servicePrincipalObjectId,
        "--assignee-principal-type", "ServicePrincipal",
        "--role", "Storage Blob Data Contributor",
        "--scope", $stateStorageId,
        "--output", "none"
    ) | Out-Null
}

$federatedCredentialName = "github-$Environment"
$existingFederatedCredential = Invoke-AzText @(
    "ad", "app", "federated-credential", "list",
    "--id", $applicationObjectId,
    "--query", "[?name=='$federatedCredentialName'].name | [0]",
    "-o", "tsv"
)

if ([string]::IsNullOrWhiteSpace($existingFederatedCredential)) {
    $subject = "repo:{0}:environment:{1}" -f $Repository, $Environment

    $credential = @{
        name        = $federatedCredentialName
        issuer      = "https://token.actions.githubusercontent.com"
        subject     = $subject
        audiences   = @("api://AzureADTokenExchange")
        description = "GitHub Actions OIDC for $Repository environment $Environment"
    } | ConvertTo-Json -Depth 4

    $temporaryFile = [System.IO.Path]::GetTempFileName()

    try {
        Set-Content -Path $temporaryFile -Value $credential -Encoding utf8

        Invoke-AzText @(
            "ad", "app", "federated-credential", "create",
            "--id", $applicationObjectId,
            "--parameters", $temporaryFile,
            "--output", "none"
        ) | Out-Null
    }
    finally {
        Remove-Item $temporaryFile -ErrorAction SilentlyContinue
    }
}

Write-Host "OIDC client id: $clientId" -ForegroundColor Green

Write-Host ""
Write-Host "Configuring GitHub staging environment..." -ForegroundColor Yellow
Invoke-Gh @("api", "--method", "PUT", "repos/$Repository/environments/$Environment", "-F", "wait_timer=0")

Invoke-Gh @("secret", "set", "AZURE_CLIENT_ID", "--repo", $Repository, "--env", $Environment, "--body", $clientId)
Invoke-Gh @("secret", "set", "AZURE_TENANT_ID", "--repo", $Repository, "--env", $Environment, "--body", $tenantId)
Invoke-Gh @("secret", "set", "AZURE_SUBSCRIPTION_ID", "--repo", $Repository, "--env", $Environment, "--body", $subscriptionId)

Invoke-Gh @("variable", "set", "TFSTATE_RESOURCE_GROUP", "--repo", $Repository, "--env", $Environment, "--body", $StateResourceGroup)
Invoke-Gh @("variable", "set", "TFSTATE_STORAGE_ACCOUNT", "--repo", $Repository, "--env", $Environment, "--body", $stateStorageAccount)
Invoke-Gh @("variable", "set", "TFSTATE_CONTAINER", "--repo", $Repository, "--env", $Environment, "--body", $StateContainer)
Invoke-Gh @("variable", "set", "AZURE_LOCATION", "--repo", $Repository, "--env", $Environment, "--body", $Location)

Write-Host ""
Write-Host "Bootstrap complete." -ForegroundColor Green
Write-Host "No application infrastructure has been applied yet."
Write-Host ""
Write-Host "Next safe command:"
Write-Host "  gh workflow run deploy-staging.yml --repo $Repository -f action=plan -f confirm_costs=false" -ForegroundColor Cyan
Write-Host ""
Write-Host "Only after reviewing the plan and expected Azure cost:"
Write-Host "  gh workflow run deploy-staging.yml --repo $Repository -f action=apply -f confirm_costs=true" -ForegroundColor Yellow

if ($RunPlan) {
    Write-Host ""
    Write-Host "Dispatching plan-only workflow..." -ForegroundColor Yellow
    Invoke-Gh @(
        "workflow", "run", "deploy-staging.yml",
        "--repo", $Repository,
        "-f", "action=plan",
        "-f", "confirm_costs=false"
    )
}
