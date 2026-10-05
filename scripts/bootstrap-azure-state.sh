#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 2 ] || [ "$#" -gt 3 ]; then
  echo "Usage: $0 <resource-group> <storage-account> [location]" >&2
  exit 2
fi

RESOURCE_GROUP="$1"
STORAGE_ACCOUNT="$2"
LOCATION="\${3:-brazilsouth}"
CONTAINER="tfstate"

if ! command -v az >/dev/null 2>&1; then
  echo "Azure CLI (az) is required." >&2
  exit 1
fi

echo "This creates Azure resources and may incur a small cost."
echo "Resource group: $RESOURCE_GROUP"
echo "Storage account: $STORAGE_ACCOUNT"
echo "Location: $LOCATION"
read -r -p "Type APPLY to continue: " CONFIRM

if [ "$CONFIRM" != "APPLY" ]; then
  echo "Cancelled."
  exit 1
fi

az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

az storage account create \
  --name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --sku Standard_LRS \
  --kind StorageV2 \
  --min-tls-version TLS1_2 \
  --allow-blob-public-access false \
  --output none

az storage container create \
  --name "$CONTAINER" \
  --account-name "$STORAGE_ACCOUNT" \
  --auth-mode login \
  --output none

echo "Remote-state bootstrap complete."
