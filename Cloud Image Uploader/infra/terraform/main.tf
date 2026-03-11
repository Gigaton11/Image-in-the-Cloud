locals {
  common_tags = merge(
    {
      Project     = var.project_name
      Environment = var.environment
      ManagedBy   = "terraform"
    },
    var.tags
  )
}

resource "aws_s3_bucket" "uploads" {
  bucket        = var.bucket_name
  force_destroy = var.force_destroy_bucket
  tags          = local.common_tags
}

resource "aws_s3_bucket_versioning" "uploads" {
  bucket = aws_s3_bucket.uploads.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "uploads" {
  bucket = aws_s3_bucket.uploads.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "uploads" {
  bucket = aws_s3_bucket.uploads.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_dynamodb_table" "file_metadata" {
  name         = var.file_metadata_table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "FileId"

  attribute {
    name = "FileId"
    type = "S"
  }

  point_in_time_recovery {
    enabled = var.enable_point_in_time_recovery
  }

  tags = local.common_tags
}

resource "aws_dynamodb_table" "download_records" {
  name         = var.download_records_table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "FileId"

  attribute {
    name = "FileId"
    type = "S"
  }

  point_in_time_recovery {
    enabled = var.enable_point_in_time_recovery
  }

  tags = local.common_tags
}

resource "aws_dynamodb_table" "user_accounts" {
  name         = var.user_accounts_table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "UserId"

  attribute {
    name = "UserId"
    type = "S"
  }

  point_in_time_recovery {
    enabled = var.enable_point_in_time_recovery
  }

  tags = local.common_tags
}

resource "aws_dynamodb_table" "password_reset_tokens" {
  name         = var.password_reset_tokens_table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "TokenId"

  attribute {
    name = "TokenId"
    type = "S"
  }

  point_in_time_recovery {
    enabled = var.enable_point_in_time_recovery
  }

  tags = local.common_tags
}

resource "aws_sesv2_email_identity" "reset_sender" {
  count          = var.enable_ses ? 1 : 0
  email_identity = var.ses_sender_email
}

data "aws_iam_policy_document" "app_access" {
  statement {
    sid = "ListBucket"

    actions = [
      "s3:ListBucket",
      "s3:ListBucketVersions"
    ]

    resources = [
      aws_s3_bucket.uploads.arn
    ]
  }

  statement {
    sid = "ManageObjects"

    actions = [
      "s3:GetObject",
      "s3:PutObject",
      "s3:DeleteObject",
      "s3:DeleteObjectVersion"
    ]

    resources = [
      "${aws_s3_bucket.uploads.arn}/*"
    ]
  }

  statement {
    sid = "DynamoCrud"

    actions = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:UpdateItem",
      "dynamodb:DeleteItem",
      "dynamodb:Query",
      "dynamodb:Scan",
      "dynamodb:DescribeTable"
    ]

    resources = [
      aws_dynamodb_table.file_metadata.arn,
      aws_dynamodb_table.download_records.arn,
      aws_dynamodb_table.user_accounts.arn,
      aws_dynamodb_table.password_reset_tokens.arn
    ]
  }

  dynamic "statement" {
    for_each = var.enable_ses ? [1] : []

    content {
      sid = "SendPasswordResetEmails"

      actions = [
        "ses:SendEmail",
        "ses:SendRawEmail"
      ]

      resources = [
        aws_sesv2_email_identity.reset_sender[0].arn
      ]
    }
  }
}

resource "aws_iam_user" "app" {
  name = var.app_iam_user_name
  tags = local.common_tags
}

resource "aws_iam_user_policy" "app_access" {
  name   = "${var.project_name}-${var.environment}-access"
  user   = aws_iam_user.app.name
  policy = data.aws_iam_policy_document.app_access.json
}

resource "aws_iam_access_key" "app" {
  user = aws_iam_user.app.name
}
