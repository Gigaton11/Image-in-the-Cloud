variable "aws_region" {
  description = "AWS region for all resources"
  type        = string
  default     = "eu-north-1"
}

variable "app_runner_region" {
  description = "AWS region for App Runner and ECR resources. Leave empty to use aws_region"
  type        = string
  default     = ""
}

variable "environment" {
  description = "Environment name (dev/stage/prod)"
  type        = string
  default     = "dev"
}

variable "project_name" {
  description = "Project tag value"
  type        = string
  default     = "cloud-image-uploader"
}

variable "bucket_name" {
  description = "Globally unique S3 bucket name for image storage"
  type        = string
}

variable "force_destroy_bucket" {
  description = "Allow bucket destroy even if it contains objects"
  type        = bool
  default     = false
}

variable "app_iam_user_name" {
  description = "IAM user name used by the application runtime"
  type        = string
  default     = "cloud-image-uploader-app"
}

variable "enable_ses" {
  description = "Create SES sender identity and grant send permissions"
  type        = bool
  default     = false
}

variable "ses_sender_email" {
  description = "Verified SES sender email address used for password reset emails"
  type        = string
  default     = ""

  validation {
    condition     = !var.enable_ses || trimspace(var.ses_sender_email) != ""
    error_message = "ses_sender_email must be set when enable_ses is true."
  }
}

variable "ses_sender_name" {
  description = "Display name used in password reset emails"
  type        = string
  default     = "Cloud Image Uploader"
}

variable "file_metadata_table_name" {
  description = "DynamoDB table name for file metadata"
  type        = string
  default     = "FileMetadata"
}

variable "download_records_table_name" {
  description = "DynamoDB table name for download records"
  type        = string
  default     = "DownloadRecords"
}

variable "user_accounts_table_name" {
  description = "DynamoDB table name for user accounts"
  type        = string
  default     = "UserAccounts"
}

variable "password_reset_tokens_table_name" {
  description = "DynamoDB table name for password reset tokens"
  type        = string
  default     = "PasswordResetTokens"
}

variable "enable_point_in_time_recovery" {
  description = "Enable PITR for DynamoDB tables"
  type        = bool
  default     = true
}

variable "tags" {
  description = "Extra tags to apply to all resources"
  type        = map(string)
  default     = {}
}

variable "enable_app_runner" {
  description = "Enable App Runner related resources (ECR repository and IAM roles)"
  type        = bool
  default     = false
}

variable "deploy_app_runner_service" {
  description = "Create the App Runner service itself. Keep false until the container image is pushed to ECR"
  type        = bool
  default     = false

  validation {
    condition     = !var.deploy_app_runner_service || var.enable_app_runner
    error_message = "deploy_app_runner_service requires enable_app_runner to be true."
  }
}

variable "app_runner_service_name" {
  description = "Name of the App Runner service"
  type        = string
  default     = "cloud-image-uploader-demo"
}

variable "app_runner_ecr_repository_name" {
  description = "Optional ECR repository name override for App Runner images"
  type        = string
  default     = ""
}

variable "app_runner_image_identifier" {
  description = "Full image identifier for App Runner (example: 123456789012.dkr.ecr.eu-west-1.amazonaws.com/repo:latest). Leave empty to use managed ECR repo + app_runner_image_tag"
  type        = string
  default     = ""
}

variable "app_runner_image_tag" {
  description = "Tag used when app_runner_image_identifier is empty"
  type        = string
  default     = "latest"
}

variable "app_runner_cpu" {
  description = "App Runner CPU units (256, 512, 1024, 2048, 4096)"
  type        = string
  default     = "1024"
}

variable "app_runner_memory" {
  description = "App Runner memory in MB (512, 1024, 2048, 3072, 4096, 6144, 8192, 10240, 12288)"
  type        = string
  default     = "2048"
}

variable "app_runner_auto_deployments_enabled" {
  description = "Enable automatic deployments on new image pushes (ECR source)"
  type        = bool
  default     = false
}

variable "app_runner_health_check_path" {
  description = "Health check path for App Runner"
  type        = string
  default     = "/"
}

variable "app_runner_environment_variables" {
  description = "Additional runtime environment variables for App Runner"
  type        = map(string)
  default     = {}
}
