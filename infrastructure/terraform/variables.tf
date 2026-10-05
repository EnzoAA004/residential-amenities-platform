variable "project_name" {
  description = "Short project identifier used in Azure resource names."
  type        = string
  default     = "resamen"
}

variable "environment" {
  description = "Deployment environment."
  type        = string
  default     = "staging"

  validation {
    condition     = contains(["staging", "production"], var.environment)
    error_message = "environment must be staging or production."
  }
}

variable "location" {
  description = "Azure region."
  type        = string
  default     = "brazilsouth"
}

variable "postgres_version" {
  description = "Azure Database for PostgreSQL major version."
  type        = string
  default     = "16"
}

variable "postgres_sku_name" {
  description = "Cost-aware PostgreSQL Flexible Server SKU for the pilot."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "postgres_storage_mb" {
  description = "PostgreSQL storage in MB."
  type        = number
  default     = 32768
}

variable "api_min_replicas" {
  description = "Minimum API replicas. Zero enables scale-to-zero."
  type        = number
  default     = 0
}

variable "api_max_replicas" {
  description = "Maximum API replicas. Staging defaults to one while startup migrations are enabled."
  type        = number
  default     = 1
}

variable "client_min_replicas" {
  description = "Minimum client replicas. Zero enables scale-to-zero."
  type        = number
  default     = 0
}

variable "client_max_replicas" {
  description = "Maximum client replicas."
  type        = number
  default     = 2
}

variable "apply_migrations_on_startup" {
  description = "Apply EF Core migrations at API startup. Intended for staging/pilot only."
  type        = bool
  default     = true
}

variable "tags" {
  description = "Additional Azure resource tags."
  type        = map(string)
  default     = {}
}
