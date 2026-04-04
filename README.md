## Image in the Cloud
Webapp for safe image sharing with temporary private or public links.
## 🟢 Live Demo
Visit [Image in the Cloud](https://cloud-image-uploader-908848556717.europe-west1.run.app/)


| Field    | Value           |
|----------|-----------------|
| Username | `Demo`          |
| Password | `demo123`       |

> The public demo is hosted on Google Cloud Run. Storage, metadata, and optional email delivery still live in AWS.

## Screenshots
<summary>After a successful upload</summary> <details><img width="1213" height="1254" alt="image" src="https://github.com/user-attachments/assets/2dc04f02-5c72-4603-9b3f-65f53977b6bc" /></details>
<summary>List of uploaded images</summary> <details><img width="1003" height="1219" alt="image" src="https://github.com/user-attachments/assets/80f8c811-05ea-44b0-954c-c8202eeea86d" /></details>


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

### *Local Deployment is on the next readme.
