locals {
  name_prefix = lower("${var.project_name}-${var.environment}")

  tags = merge(
    {
      project     = "residential-amenities-platform"
      environment = var.environment
      managed-by  = "terraform"
      portfolio   = "true"
    },
    var.tags
  )

  api_app_name    = "${local.name_prefix}-api"
  client_app_name = "${local.name_prefix}-web"

  client_host   = "${local.client_app_name}.${azurerm_container_app_environment.main.default_domain}"
  client_origin = "https://${local.client_host}"
}
