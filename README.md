# Image in the Cloud

## [🔴 Live Demo](https://9q4mjgxsfd.eu-west-1.awsapprunner.com/) 

| Field          | Value            |
|----------------|------------------|
| Username       | `Demo`           |
| Email          | `test@aws.demo`  |
| Password       | `demo123`        |

> Video demo coming soon.

---

## Description

Image in the Cloud is an ASP.NET Core (.NET 10) application for temporary uploading, sharing and managing images with AWS-backed storage and metadata.

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
- Account registration, Login-Logout, (forgot/reset with SES implementation) 

## Tech Stack

- .NET 10 (ASP.NET Core MVC)
- AWS S3 (object storage)
- AWS DynamoDB (metadata, users, reset tokens, download logs)
- AWS SES v2 (optional password reset email delivery) (To Be Added)
- SixLabors ImageSharp (image processing)
- Terraform (optional AWS infrastructure provisioning)
