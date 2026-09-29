provider "aws" {
  region  = "us-east-1"

  default_tags {
    tags = {
      Environment = "live"
      Application = "giftexchange"
      ManagedBy   = "terraform"
      Owner       = "giftexchange@osbornesupremacy.com"
    }
  }
}

terraform {
  required_providers {
    aws = {
      source = "hashicorp/aws"
    }
    random = {
      source = "hashicorp/random"
    }
  }

  backend "s3" {
    bucket       = "bro-tfstate"
    use_lockfile = true
    key          = "giftexchange/live"
    region       = "us-east-1"
  }
}
