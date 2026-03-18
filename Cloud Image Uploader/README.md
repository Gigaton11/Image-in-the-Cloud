# Cloud Image Uploader

## Live Demo

### [Image in the Cloud (Google Cloud Run)](https://cloud-image-uploader-908848556717.europe-west1.run.app/)

| Field    | Value           |
|----------|-----------------|
| Username | `Demo`          |
| Email    | `test@aws.demo` |
| Password | `demo123`       |

> The public demo is hosted on Google Cloud Run. Storage, metadata, and optional email delivery still live in AWS.

## Overview

Cloud Image Uploader is an ASP.NET Core MVC app for short-lived image sharing.

Each upload stores:

- the original image for downloads
- a web-optimized WebP variant
- a thumbnail WebP variant
- metadata, ownership, expiry, and download history in DynamoDB

The app supports guest uploads for quick sharing and authenticated uploads for private access, longer retention windows, and an owner gallery.

## Current Capabilities

- Drag-and-drop uploads with progress feedback
- Guest uploads with a fixed 10-minute lifetime
- Authenticated uploads with 10 minutes, 1 hour, 6 hours, or 1 day retention
- Public/private visibility controls for signed-in users
- Original download plus thumbnail and optimized web variants
- Password reset flow with optional SES delivery and development fallback links
- In-process expiry scheduling plus a periodic cleanup safety-net scan
- My Images gallery for signed-in users

## Request Flow

1. `HomeController` validates the upload and resolves retention plus visibility.
2. `ImageProcessingService` creates the WebP web variant and thumbnail.
3. `S3Service` uploads the original file and both processed variants to S3.
4. `DynamoDbService` stores metadata, users, password reset tokens, and download records in DynamoDB.
5. `FileDeletionSchedulerService` schedules immediate expiry cleanup, while `ExpiredFileCleanupService` catches anything missed after restarts.

## Tech Stack

- .NET 10 ASP.NET Core MVC
- AWS S3 for object storage
- AWS DynamoDB for metadata, users, password reset tokens, and download records
- AWS SES v2 for optional password reset delivery
- SixLabors ImageSharp for image processing
- Terraform for optional AWS infrastructure provisioning
- Docker and docker compose for local container runs
- Google Cloud Run or AWS App Runner for hosting

## Local Development

### Prerequisites

- .NET 10 SDK
- AWS credentials with access to S3, DynamoDB, and optionally SES
- An existing S3 bucket
- Existing DynamoDB tables, or permission to create them during development
- Terraform only if you want this repo to provision the AWS resources

If local HTTPS is not trusted yet:

```bash
dotnet dev-certs https --trust
```

### Restore dependencies

```bash
dotnet restore
```

### Configure local secrets

This project already has a `UserSecretsId`, so only the values need to be set:

```bash
dotnet user-secrets set "AWS:AccessKey" "<your-access-key>"
dotnet user-secrets set "AWS:SecretKey" "<your-secret-key>"
dotnet user-secrets set "AWS:Region" "eu-north-1"
dotnet user-secrets set "AWS:BucketName" "<existing-s3-bucket-name>"
dotnet user-secrets set "AWS:AutoCreateTables" "false"
```

Optional email settings:

```bash
dotnet user-secrets set "Email:EnableSesDelivery" "false"
dotnet user-secrets set "Email:FromAddress" "verified-sender@example.com"
dotnet user-secrets set "Email:FromName" "Cloud Image Uploader"
dotnet user-secrets set "Email:ShowResetLinkInDevelopment" "true"
```

Use `AWS:AutoCreateTables = true` only during development when the tables do not exist yet.
Outside Development the app ignores that setting and logs a warning.

### Run locally

```bash
dotnet run
```

Useful routes:

- `GET /` upload page
- `POST /Home/Upload` upload action
- `GET /my-images` signed-in gallery
- `GET /download/{fileId}` original download endpoint
- `GET /thumbnail/{fileId}` thumbnail endpoint
- `POST /delete/{fileId}` delete endpoint
- `POST /visibility/{fileId}` visibility toggle
- `GET /ping` lightweight health check

## Local Container Run

The checked-in `docker-compose.yml` is meant for local container testing against existing AWS resources.

Create a local `.env` file in the project directory with:

```dotenv
AWS_ACCESS_KEY=<your-access-key>
AWS_SECRET_KEY=<your-secret-key>
AWS_BUCKET_NAME=<existing-s3-bucket-name>
```

Then run:

```bash
docker compose up --build
```

The container serves plain HTTP on `http://localhost:8080`.
`UseHttps=false` is intentional here because TLS is not terminated inside the local container.

## Configuration Notes

- Set both `AWS:AccessKey` and `AWS:SecretKey`, or neither. A half-configured static credential pair fails startup.
- `UseForwardedHeaders` defaults to `true` automatically on Cloud Run because the app detects `K_SERVICE`.
- App Runner already injects `UseHttps=false`, `UseForwardedHeaders=true`, and `ASPNETCORE_URLS=http://0.0.0.0:8080` through Terraform.
- `Email:ShowResetLinkInDevelopment` keeps the reset flow testable when SES delivery is disabled or not configured.
- Cleanup is handled in-process. On scale-to-zero platforms such as Cloud Run, expired files may linger until the next request wakes an instance because this branch does not expose a public scheduler-trigger endpoint.

## Deployment

### AWS App Runner via Terraform

Infrastructure lives under `infra/terraform`.

1. Copy `infra/terraform/terraform.tfvars.template` to `infra/terraform/terraform.tfvars`.
2. Fill in the bucket name, tags, and any App Runner or SES overrides.
3. Run Terraform once with `deploy_app_runner_service = false` so shared AWS resources and ECR are created first.
4. Build and push a container image to the managed ECR repository.
5. Set `deploy_app_runner_service = true` and apply again to create the App Runner service.

This two-phase rollout avoids App Runner failing on an image that does not exist yet.

### Google Cloud Run (hosting only)

This path keeps AWS S3, DynamoDB, and SES unchanged and only moves the web host to Google Cloud Run.

Prerequisites:

- Google Cloud SDK (`gcloud`)
- Docker
- A Google Cloud project with billing enabled
- Existing AWS credentials and resources for this app

Enable APIs:

```bash
gcloud services enable run.googleapis.com artifactregistry.googleapis.com secretmanager.googleapis.com
```

Create an Artifact Registry repository:

```bash
gcloud artifacts repositories create cloud-image-uploader \
  --repository-format=docker \
  --location=europe-west1 \
  --description="Container images for Cloud Image Uploader"
```

Store AWS credentials in Secret Manager:

```bash
printf "%s" "<aws-access-key>" | gcloud secrets create aws-access-key --data-file=-
printf "%s" "<aws-secret-key>" | gcloud secrets create aws-secret-key --data-file=-
```

Build and push the image:

```bash
gcloud auth configure-docker europe-west1-docker.pkg.dev
docker build -t europe-west1-docker.pkg.dev/<PROJECT_ID>/cloud-image-uploader/cloud-image-uploader:latest .
docker push europe-west1-docker.pkg.dev/<PROJECT_ID>/cloud-image-uploader/cloud-image-uploader:latest
```

Deploy one revision:

```bash
gcloud run deploy cloud-image-uploader \
  --image europe-west1-docker.pkg.dev/<PROJECT_ID>/cloud-image-uploader/cloud-image-uploader:latest \
  --region europe-west1 \
  --platform managed \
  --port 8080 \
  --cpu 1 \
  --memory 2Gi \
  --min-instances 0 \
  --max-instances 1 \
  --allow-unauthenticated \
  --set-env-vars "ASPNETCORE_URLS=http://0.0.0.0:8080,UseHttps=false,AWS__Region=eu-north-1,AWS__BucketName=<S3_BUCKET>,AWS__AutoCreateTables=false,Email__EnableSesDelivery=false" \
  --update-secrets "AWS__AccessKey=aws-access-key:latest,AWS__SecretKey=aws-secret-key:latest"
```

Cloud Run will auto-enable forwarded-header handling through the app's `K_SERVICE` detection, so `UseForwardedHeaders` does not need to be set explicitly unless you want to force it.