provider "azurerm" {
  # Provider registration is handled by the account bootstrap under the
  # subscription owner context. The GitHub deployment identity is intentionally
  # scoped to the staging resource group and cannot register providers at the
  # subscription scope.
  resource_provider_registrations = "none"

  features {}
}
