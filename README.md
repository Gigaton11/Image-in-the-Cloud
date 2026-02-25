# Cloud Image Uploader – AWS-Powered Demo

A simple, full-stack ASP.NET Core MVC web application that demonstrates secure image uploading to **Amazon S3**, metadata & download tracking with **Amazon DynamoDB**, and best-practice credential handling using **AWS Secrets Manager** (ready for production).

This project was built as a learning/demo showcase of how a real-world serverless-ish image hosting service can be implemented on AWS without ever hard-coding credentials.

## Features

- Upload JPG, PNG, and WebP images (max 10 MB)
- Files are stored securely in an S3 bucket
- Each upload generates a short-lived **pre-signed URL** (10-minute expiry)
- All uploads and downloads are tracked in DynamoDB tables
- Clean separation of concerns with dedicated service classes
- Ready for **AWS Secrets Manager** integration (no keys in code or appsettings in production)
- Local development works with regular AWS credentials in `appsettings.json`

## Tech Stack

- ASP.NET Core 8 MVC (.NET 8)
- Amazon S3 (via AWS SDK for .NET)
- Amazon DynamoDB (via high-level `DynamoDBContext`)
- AWS Secrets Manager (optional but implemented)
- TransferUtility for efficient multipart uploads

## Project Structure
Cloud_Image_Uploader/
├── Controllers/
│   └── HomeController.cs          # Upload/Download endpoints
├── Services/
│   ├── S3Service.cs               # S3 upload, download, presigned URLs
│   ├── DynamoDbService.cs         # Tracks uploads & downloads
│   └── AwsSecretsService.cs       # Retrieves secrets from Secrets Manager
├── Models/
│   ├── FileMetadata.cs            # DynamoDB table model
│   └── DownloadRecord.cs
├── Views/Home/Index.cshtml        # Simple upload form + result display
├── Program.cs                     # DI & AWS service registration
└── appsettings.json               # Local dev credentials (leave empty in prod)

## Prerequisites

- .NET 8 SDK
- An AWS account
- AWS CLI configured (or credentials in `~/.aws/credentials`)
- S3 bucket (any name/region)
- Two DynamoDB tables:
  - `FileMetadata` (Partition key: `FileId` (string))
  - `DownloadRecords` (Partition key: `FileId` (string))

## Local Development Setup

1. **Clone the repo**
   ```bash
   git clone https://github.com/yourusername/Cloud-Image-Uploader.git
   cd Cloud-Image-Uploader



-Fill in your AWS details in appsettings.json
{
  "AWS": {
    "AccessKey": "AKIAxxxxxxxxxxxxxxxx",
    "SecretKey": "your-secret-key-here",
    "Region": "us-east-1",
    "BucketName": "your-unique-bucket-name"
  }
}
    




-Create the DynamoDB tables (once)
aws dynamodb create-table --table-name FileMetadata \
  --attribute-definitions AttributeName=FileId,AttributeType=S \
  --key-schema AttributeName=FileId,KeyType=HASH \
  --billing-mode PAY_PER_REQUEST

aws dynamodb create-table --table-name DownloadRecords \
  --attribute-definitions AttributeName=FileId,AttributeType=S \
  --key-schema AttributeName=FileId,KeyType=HASH \
  --billing-mode PAY_PER_REQUEST


-Run the app
    (Open https://localhost:5001 or http://localhost:5000)

-Upload an image → you’ll get a temporary signed URL (valid 10 min) and see it displayed.





Production-Ready Improvements (Already Partially Implemented)

Replace hard-coded credentials with AWS Secrets Manager (see AwsSecretsService.cs)
Use IAM Roles (EC2/ECS/EKS/Lambda) instead of access keys
Add authentication (currently tracks "anonymous" or claims-based user)
Serve images via CloudFront + S3 for global low-latency delivery
Add rate limiting, virus scanning, image processing (Sharp/Lambda), etc.

Using Secrets Manager (Production)

Store your credentials as a JSON secret:
{"AccessKey":"AKIA...","SecretKey":"...","BucketName":"my-bucket","Region":"us-east-1"}


2.Update S3Service constructor to read from AwsSecretsService instead of IConfiguration.(The service class is already written – just wire it up!)


Endpoints

Method      Route                    Description
GET         /                        Upload form
POST        /Home/Upload             Upload image → returns presigned URL
GET         /download/{fileId}       Download file (tracks download in DynamoDB)
GET         /ping                    "Health check (""Server is running"")"


