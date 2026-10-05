output "resource_group_name" {
  value = data.azurerm_resource_group.main.name
}

output "acr_name" {
  value = azurerm_container_registry.main.name
}

output "acr_login_server" {
  value = azurerm_container_registry.main.login_server
}

output "api_container_app_name" {
  value = azurerm_container_app.api.name
}

output "api_internal_fqdn" {
  value = local.api_host
}

output "client_container_app_name" {
  value = azurerm_container_app.client.name
}

output "client_url" {
  value = local.client_origin
}

output "storage_account_name" {
  value = azurerm_storage_account.app.name
}

output "postgres_server_fqdn" {
  value     = azurerm_postgresql_flexible_server.main.fqdn
  sensitive = true
}
