output "aws_region" {
  description = "AWS region where resources were deployed"
  value       = var.aws_region
}

output "app_runner_region" {
  description = "AWS region used for App Runner and ECR resources"
  value       = trimspace(var.app_runner_region) != "" ? trimspace(var.app_runner_region) : var.aws_region
}

output "bucket_name" {
  description = "S3 bucket used by the uploader"
  value       = aws_s3_bucket.uploads.bucket
}

output "file_metadata_table_name" {
  description = "DynamoDB table for file metadata"
  value       = aws_dynamodb_table.file_metadata.name
}

output "download_records_table_name" {
  description = "DynamoDB table for download logs"
  value       = aws_dynamodb_table.download_records.name
}

output "user_accounts_table_name" {
  description = "DynamoDB table for user accounts"
  value       = aws_dynamodb_table.user_accounts.name
}

output "password_reset_tokens_table_name" {
  description = "DynamoDB table for reset tokens"
  value       = aws_dynamodb_table.password_reset_tokens.name
}

output "ses_sender_email" {
  description = "SES sender identity used for password reset emails"
  value       = var.enable_ses ? aws_sesv2_email_identity.reset_sender[0].email_identity : null
}

output "app_runner_ecr_repository_url" {
  description = "ECR repository URL used for App Runner container images"
  value       = var.enable_app_runner ? aws_ecr_repository.app_runner[0].repository_url : null
}

output "app_runner_service_arn" {
  description = "ARN of the App Runner service"
  value       = length(aws_apprunner_service.webapp) > 0 ? aws_apprunner_service.webapp[0].arn : null
}

output "app_runner_service_url" {
  description = "Public URL of the App Runner service"
  value       = length(aws_apprunner_service.webapp) > 0 ? aws_apprunner_service.webapp[0].service_url : null
}

output "app_runner_instance_role_arn" {
  description = "IAM role assumed by the App Runner runtime"
  value       = var.enable_app_runner ? aws_iam_role.apprunner_instance[0].arn : null
}

output "app_access_key_id" {
  description = "Access key ID for the application IAM user"
  value       = aws_iam_access_key.app.id
}

output "app_secret_access_key" {
  description = "Secret access key for the application IAM user"
  value       = aws_iam_access_key.app.secret
  sensitive   = true
}
