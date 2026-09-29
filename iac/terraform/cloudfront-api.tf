# CloudFront distribution for API Gateway (api.namesoutofahat.com)
resource "aws_cloudfront_distribution" "api" {
  enabled         = true
  is_ipv6_enabled = true
  comment         = "CloudFront distribution for Gift Exchange API Gateway"
  price_class     = "PriceClass_All"
  aliases         = ["api.namesoutofahat.com"]

  origin {
    domain_name = "${aws_api_gateway_rest_api.giftexchange-gateway.id}.execute-api.${data.aws_region.current.region}.amazonaws.com"
    origin_id   = "APIGateway-${aws_api_gateway_rest_api.giftexchange-gateway.id}"
    origin_path = "/${aws_api_gateway_stage.live-stage.stage_name}"

    # Proves to the application that a request came through here, and so past the web ACL and the
    # geographic restriction. See OriginGuard.
    custom_header {
      name  = "X-Origin-Verify"
      value = random_password.origin_verify.result
    }

    custom_origin_config {
      http_port                = 80
      https_port               = 443
      origin_protocol_policy   = "https-only"
      origin_ssl_protocols     = ["TLSv1.2"]
      origin_keepalive_timeout = 5
      origin_read_timeout      = 30
    }
  }

  default_cache_behavior {
    allowed_methods  = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "APIGateway-${aws_api_gateway_rest_api.giftexchange-gateway.id}"

    viewer_protocol_policy = "redirect-to-https"
    compress               = true

    # CachingDisabled - all requests pass through to API Gateway
    cache_policy_id = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad"

    # AllViewerExceptHostHeader - forward all headers except Host, plus query strings and cookies
    origin_request_policy_id = "b689b0a8-53d0-40ab-baf2-68738e2966ac"
  }

  restrictions {
    geo_restriction {
      restriction_type = "whitelist"
      locations        = ["US", "CA"]
    }
  }

  viewer_certificate {
    acm_certificate_arn      = aws_acm_certificate.api_prod.arn
    ssl_support_method       = "sni-only"
    minimum_protocol_version = "TLSv1.2_2021"
  }

  web_acl_id = data.aws_wafv2_web_acl.cloudfront_managed_pro.arn

  tags = {
    Name = "api-distribution"
  }

  depends_on = [
    aws_acm_certificate_validation.api_prod,
    aws_api_gateway_domain_name.api_prod
  ]
}

# This Web ACL is created and managed by AWS CloudFront
data "aws_wafv2_web_acl" "cloudfront_managed_pro" {
  name  = "CreatedByCloudFront-5775ad2d"
  scope = "CLOUDFRONT"
}


# The value CloudFront presents to the API on every request, and OriginGuard checks for.
#
# Unlike the session signing key, this one has to be in Terraform state: CloudFront's own
# configuration carries it, and Terraform owns that. It is acceptable there because all it can do is
# let a request past the check that it came through CloudFront -- it signs nothing and identifies
# nobody. Rotate it by tainting this resource and applying.
resource "random_password" "origin_verify" {
  length  = 48
  special = false
}

locals {
  # Off for the first apply, which introduces the header: CloudFront takes minutes to start sending
  # it and the Lambda's configuration changes in seconds, so enforcing straight away would refuse
  # real traffic in between. Once the router has stopped logging "did not come through CloudFront"
  # for real requests, set this to true and apply again.
  origin_verify_enforced = false
}
