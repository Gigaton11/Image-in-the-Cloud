output "aws_region" {
  description = "AWS region where resources were deployed"
  value       = var.aws_region
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

output "app_access_key_id" {
  description = "Access key ID for the application IAM user"
  value       = aws_iam_access_key.app.id
}

output "app_secret_access_key" {
  description = "Secret access key for the application IAM user"
  value       = aws_iam_access_key.app.secret
  sensitive   = true
}
