# Cloud Image Uploader

Image in the Cloud is an ASP.NET Core MVC application for uploading, optimizing, sharing, and managing images with AWS-backed storage and metadata.

It includes account-based access, image processing (original + web + thumbnail variants), time-based retention, and optional password reset email delivery via SES.

## Core Features

- Drag-and-drop upload with live progress feedback
- Image processing pipeline (original, web-optimized WebP, thumbnail WebP)
- Guest and authenticated upload modes
- Retention controls:
  - Guest: 10 minutes
  - Signed-in: 10 minutes, 1 hour, 6 hours, 1 day
- Visibility controls for signed-in users (public/private)
- Personal image library page for signed-in users
- Secure download and thumbnail endpoints with ownership checks
- Background cleanup for expired files
- Account registration, login, logout, forgot/reset password flow

## Tech Stack

- .NET 10 (ASP.NET Core MVC)
- AWS S3 (object storage)
- AWS DynamoDB (metadata, users, reset tokens, download logs)
- AWS SES v2 (optional password reset email delivery)
- SixLabors ImageSharp (image processing)
- Terraform (optional AWS infrastructure provisioning)

## Quick Start

### 1) Prerequisites

- .NET 10 SDK
- AWS account with permissions for S3, DynamoDB, and optionally SES
- Terraform (optional, only if provisioning infra from this repo)

### 2) Restore dependencies

```bash
dotnet restore
```

### 3) Configure secrets (local development)

For local runs, the project uses user-secrets. Set at least:

```bash
dotnet user-secrets set "AWS:AccessKey" "<your-access-key>"
dotnet user-secrets set "AWS:SecretKey" "<your-secret-key>"
dotnet user-secrets set "AWS:Region" "eu-north-1"
dotnet user-secrets set "AWS:BucketName" "<your-bucket-name>"
dotnet user-secrets set "AWS:AutoCreateTables" "false"
```

In AWS App Runner, do not set static access keys in app config. The app supports IAM role credentials automatically.

Optional email settings:

```bash
dotnet user-secrets set "Email:EnableSesDelivery" "false"
dotnet user-secrets set "Email:FromAddress" "verified-sender@example.com"
dotnet user-secrets set "Email:FromName" "Cloud Image Uploader"
dotnet user-secrets set "Email:ShowResetLinkInDevelopment" "true"
```

### 4) Run the app

```bash
dotnet run
```

Then open the local URL printed by ASP.NET Core.

## Main Routes

- `GET /` Upload page
- `GET /my-images` Signed-in image library
- `POST /` Upload image
- `GET /download/{fileId}` Download image
- `GET /thumbnail/{fileId}` Fetch thumbnail
- `POST /delete/{fileId}` Delete image
- `POST /visibility/{fileId}` Change image visibility
- `GET /Account/Login`
- `GET /Account/Register`
- `GET /Account/ForgotPassword`
- `GET /Account/ResetPassword`

## Terraform (Optional)

Terraform files live in `infra/terraform` and provision:

- S3 bucket
- DynamoDB tables
- IAM user + policy + access key
- Optional SES sender identity
- Optional App Runner deployment resources (ECR repo, IAM roles, App Runner service)

Typical workflow:

```bash
cd infra/terraform
terraform init
terraform plan -var-file="terraform.tfvars"
terraform apply -var-file="terraform.tfvars"
```

Start from `terraform.tfvars.example` and create your own `terraform.tfvars` (which should not be committed).

## Deploy Live Demo on AWS App Runner

This repo supports a two-phase App Runner deployment so you can create infra first, then deploy after pushing your image.

### 1) Prepare Terraform variables

Create `infra/terraform/terraform.tfvars` from `terraform.tfvars.example` and set at least:

- `bucket_name` to a globally unique value
- `enable_app_runner = true`
- `deploy_app_runner_service = false`

Keep DynamoDB table names at defaults unless you also update model attributes in code.

### 2) Apply base infrastructure

```bash
cd infra/terraform
terraform init
terraform apply -var-file="terraform.tfvars"
```

This creates S3, DynamoDB, IAM resources, and an ECR repository.

### 3) Build and push container image to ECR

From the repository root:

```bash
AWS_REGION="eu-north-1"
AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
ECR_REPO=$(cd infra/terraform && terraform output -raw app_runner_ecr_repository_url)

aws ecr get-login-password --region "$AWS_REGION" | docker login --username AWS --password-stdin "$AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com"
docker build -t cloud-image-uploader:latest .
docker tag cloud-image-uploader:latest "$ECR_REPO:latest"
docker push "$ECR_REPO:latest"
```

On Windows PowerShell:

```powershell
$AWS_REGION = "eu-north-1"
$AWS_ACCOUNT_ID = aws sts get-caller-identity --query Account --output text
Push-Location infra/terraform
$ECR_REPO = terraform output -raw app_runner_ecr_repository_url
Pop-Location

aws ecr get-login-password --region $AWS_REGION | docker login --username AWS --password-stdin "$AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com"
docker build -t cloud-image-uploader:latest .
docker tag cloud-image-uploader:latest "${ECR_REPO}:latest"
docker push "${ECR_REPO}:latest"
```

### 4) Deploy the App Runner service

Set `deploy_app_runner_service = true` in `infra/terraform/terraform.tfvars`, then run:

```bash
cd infra/terraform
terraform apply -var-file="terraform.tfvars"
```

Get your live URL:

```bash
terraform output -raw app_runner_service_url
```

If the output does not include protocol, open it as `https://<app_runner_service_url>`.

### 5) Verify live demo

- Open the App Runner URL.
- Upload an image and test download.
- Check that records are created in `FileMetadata` and `DownloadRecords` tables.

## Repository Hygiene

A root `.gitignore` has been added to exclude generated and sensitive artifacts, including:

- .NET build outputs and local caches (`bin`, `obj`, `.nuget`, `.dotnet`)
- Terraform local working directory and state files (`.terraform`, `*.tfstate`, `*.tfvars`)
- IDE-specific files (`.vs`, `.vscode`, `.idea`, `*.user`)

## Security Notes

- Never commit AWS credentials, Terraform state, or local tfvars files.
- If credentials are ever exposed, rotate them immediately.
- Use least-privilege IAM policies in production.
- Keep `Email:ShowResetLinkInDevelopment` disabled outside development.

## Development Notes

- App logs are emitted with UTC timestamps for easier expiry debugging.
- `AWS:AutoCreateTables` can be enabled to create required DynamoDB tables at startup.
- Expired file cleanup runs in the background and also has per-file scheduled deletion as a fast path.
