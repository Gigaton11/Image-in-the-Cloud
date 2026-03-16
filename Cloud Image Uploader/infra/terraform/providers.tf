terraform {
  required_version = ">= 1.6.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

# App Runner and ECR are not available in all regions (e.g. eu-north-1).
# This aliased provider targets a separate region for those resources.
# All App Runner / ECR resources reference it via `provider = aws.app_runner`.
provider "aws" {
  alias  = "app_runner"
  region = trimspace(var.app_runner_region) != "" ? trimspace(var.app_runner_region) : var.aws_region
}
