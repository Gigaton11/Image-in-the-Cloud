locals {
  common_tags = merge(
    {
      Project     = var.project_name
      Environment = var.environment
      ManagedBy   = "terraform"
    },
    var.tags
  )

  # True only when both the App Runner flag and the service-deployment flag are set.
  # Kept as a separate flag so the ECR repo and IAM roles can be created first (phase 1),
  # then the service added after the image is pushed (phase 2).
  app_runner_service_enabled = var.enable_app_runner && var.deploy_app_runner_service
}

# ---------------------------------------------------------------------------
# S3 — image storage bucket
# Versioning is enabled so S3Service.DeleteFileAsync can hard-delete all
# object versions and delete markers when the app purges an expired file.
# Public access is fully blocked; all reads go through the application layer.
# ---------------------------------------------------------------------------

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

# ---------------------------------------------------------------------------
# DynamoDB — application tables
# All tables use on-demand billing and have PITR enabled by default.
# Table names must stay in sync with the [DynamoDBTable] attributes in the
# C# model classes (FileMetadata, DownloadRecord, UserAccount, PasswordResetToken).
# ---------------------------------------------------------------------------
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

# ---------------------------------------------------------------------------
# SES — email identity for password-reset delivery (optional)
# Only created when enable_ses = true. The verified identity ARN is attached
# to the IAM policy below so the app can call ses:SendEmail.
# ---------------------------------------------------------------------------
resource "aws_sesv2_email_identity" "reset_sender" {
  count          = var.enable_ses ? 1 : 0
  email_identity = var.ses_sender_email
}

# ---------------------------------------------------------------------------
# IAM — application user and least-privilege access policy
# The IAM user is used for local dev and non-role-based deployments.
# App Runner workloads use the instance IAM role further below instead.
# ---------------------------------------------------------------------------
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

# ---------------------------------------------------------------------------
# App Runner — ECR repository, IAM roles, and service (all optional)
# Deployment is two-phase: create ECR + IAM roles first (enable_app_runner = true,
# deploy_app_runner_service = false), push the container image, then set
# deploy_app_runner_service = true and apply again to create the service.
# ---------------------------------------------------------------------------
resource "aws_ecr_repository" "app_runner" {
  provider = aws.app_runner
  count    = var.enable_app_runner ? 1 : 0

  name                 = trimspace(var.app_runner_ecr_repository_name) != "" ? var.app_runner_ecr_repository_name : "${var.project_name}-${var.environment}"
  image_tag_mutability = "MUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }

  encryption_configuration {
    encryption_type = "AES256"
  }

  tags = local.common_tags
}

data "aws_iam_policy_document" "apprunner_ecr_assume_role" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["build.apprunner.amazonaws.com"]
    }
  }
}

data "aws_iam_policy_document" "apprunner_instance_assume_role" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["tasks.apprunner.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "apprunner_ecr_access" {
  count = var.enable_app_runner ? 1 : 0

  name               = "${var.project_name}-${var.environment}-apprunner-ecr-access"
  assume_role_policy = data.aws_iam_policy_document.apprunner_ecr_assume_role.json
  tags               = local.common_tags
}

resource "aws_iam_role_policy_attachment" "apprunner_ecr_access" {
  count = var.enable_app_runner ? 1 : 0

  role       = aws_iam_role.apprunner_ecr_access[0].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSAppRunnerServicePolicyForECRAccess"
}

resource "aws_iam_role" "apprunner_instance" {
  count = var.enable_app_runner ? 1 : 0

  name               = "${var.project_name}-${var.environment}-apprunner-instance"
  assume_role_policy = data.aws_iam_policy_document.apprunner_instance_assume_role.json
  tags               = local.common_tags
}

resource "aws_iam_role_policy" "apprunner_instance_access" {
  count = var.enable_app_runner ? 1 : 0

  name   = "${var.project_name}-${var.environment}-apprunner-runtime-access"
  role   = aws_iam_role.apprunner_instance[0].id
  policy = data.aws_iam_policy_document.app_access.json
}

resource "aws_apprunner_service" "webapp" {
  provider = aws.app_runner
  count    = local.app_runner_service_enabled ? 1 : 0

  service_name = var.app_runner_service_name

  source_configuration {
    auto_deployments_enabled = var.app_runner_auto_deployments_enabled

    authentication_configuration {
      access_role_arn = aws_iam_role.apprunner_ecr_access[0].arn
    }

    image_repository {
      image_repository_type = "ECR"
      image_identifier      = trimspace(var.app_runner_image_identifier) != "" ? trimspace(var.app_runner_image_identifier) : "${aws_ecr_repository.app_runner[0].repository_url}:${var.app_runner_image_tag}"

      image_configuration {
        port = "8080"

        runtime_environment_variables = merge(
          {
            ASPNETCORE_URLS          = "http://0.0.0.0:8080"
            UseHttps                 = "false"
            UseForwardedHeaders      = "true"
            AWS__Region              = var.aws_region
            AWS__BucketName          = aws_s3_bucket.uploads.bucket
            AWS__AutoCreateTables    = "false"
            Email__EnableSesDelivery = tostring(var.enable_ses)
          },
          var.enable_ses ? {
            Email__FromAddress = var.ses_sender_email
            Email__FromName    = var.ses_sender_name
          } : {},
          var.app_runner_environment_variables
        )
      }
    }
  }

  instance_configuration {
    cpu               = var.app_runner_cpu
    memory            = var.app_runner_memory
    instance_role_arn = aws_iam_role.apprunner_instance[0].arn
  }

  health_check_configuration {
    protocol            = "HTTP"
    path                = var.app_runner_health_check_path
    healthy_threshold   = 1
    unhealthy_threshold = 5
    interval            = 10
    timeout             = 5
  }

  tags = local.common_tags

  depends_on = [
    aws_iam_role_policy_attachment.apprunner_ecr_access,
    aws_iam_role_policy.apprunner_instance_access
  ]
}
