variable "aws_region" {
  description = "AWS region for all resources"
  type        = string
  default     = "eu-north-1"
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
    condition     = !var.enable_ses || trim(var.ses_sender_email) != ""
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
