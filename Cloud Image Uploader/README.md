# Cloud Image Uploader

Cloud Image Uploader is an ASP.NET Core MVC application for uploading, optimizing, sharing, and managing images with AWS-backed storage and metadata.

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

### 3) Configure secrets

The project uses user-secrets in development. Set at least:

```bash
dotnet user-secrets set "AWS:AccessKey" "<your-access-key>"
dotnet user-secrets set "AWS:SecretKey" "<your-secret-key>"
dotnet user-secrets set "AWS:Region" "eu-north-1"
dotnet user-secrets set "AWS:BucketName" "<your-bucket-name>"
dotnet user-secrets set "AWS:AutoCreateTables" "false"
```

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

Typical workflow:

```bash
cd infra/terraform
terraform init
terraform plan -var-file="terraform.tfvars"
terraform apply -var-file="terraform.tfvars"
```

Start from `terraform.tfvars.example` and create your own `terraform.tfvars` (which should not be committed).

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
