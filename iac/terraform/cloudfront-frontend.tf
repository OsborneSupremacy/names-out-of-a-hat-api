# CloudFront Origin Access Control for S3
resource "aws_cloudfront_origin_access_control" "frontend" {
  name                              = "frontend-oac"
  description                       = "Origin Access Control for frontend S3 bucket"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

# Viewer-request rewrite that puts SPA routes onto /index.html. See the function's own file for
# why this is done here rather than with custom error responses.
resource "aws_cloudfront_function" "frontend_security_headers" {
  name    = "frontend-security-headers"
  runtime = "cloudfront-js-2.0"
  comment = "Adds CSP, HSTS and the other security headers to every site response"
  publish = true
  code    = file("${path.module}/functions/frontend-security-headers.js")
}

resource "aws_cloudfront_function" "frontend_spa_router" {
  name    = "frontend-spa-router"
  runtime = "cloudfront-js-2.0"
  comment = "Rewrites SPA route paths to /index.html and leaves file paths to 404"
  publish = true
  code    = file("${path.module}/functions/frontend-spa-router.js")
}

# CloudFront distribution for frontend (namesoutofahat.com and www)
resource "aws_cloudfront_distribution" "frontend" {
  enabled             = true
  is_ipv6_enabled     = true
  default_root_object = "index.html"
  price_class         = "PriceClass_All"
  aliases             = ["namesoutofahat.com", "www.namesoutofahat.com"]

  origin {
    domain_name              = aws_s3_bucket.frontend.bucket_regional_domain_name
    origin_id                = "S3-${aws_s3_bucket.frontend.id}"
    origin_access_control_id = aws_cloudfront_origin_access_control.frontend.id
  }

  default_cache_behavior {
    allowed_methods        = ["GET", "HEAD", "OPTIONS"]
    cached_methods         = ["GET", "HEAD"]
    target_origin_id       = "S3-${aws_s3_bucket.frontend.id}"
    viewer_protocol_policy = "redirect-to-https"
    compress               = true
    cache_policy_id        = "658327ea-f89d-4fab-a63d-7e88639e58f6" # Managed-CachingOptimized

    # Runs before the cache key is computed, so every route shares the one /index.html entry.
    function_association {
      event_type   = "viewer-request"
      function_arn = aws_cloudfront_function.frontend_spa_router.arn
    }

    # On the way out, for every response including errors. See the function for why it is not a
    # response headers policy.
    function_association {
      event_type   = "viewer-response"
      function_arn = aws_cloudfront_function.frontend_security_headers.arn
    }
  }

  # No custom_error_response blocks by design. Routing is handled on the way in by
  # aws_cloudfront_function.frontend_spa_router, so an error reaching this point is a real one and
  # is passed to the caller with its own status. Rewriting 404 and 403 to "200 /index.html" was what
  # made a missing file indistinguishable from a working page.

  restrictions {
    geo_restriction {
      restriction_type = "whitelist"
      locations        = ["US", "CA"]
    }
  }

  viewer_certificate {
    acm_certificate_arn      = aws_acm_certificate.frontend.arn
    ssl_support_method       = "sni-only"
    minimum_protocol_version = "TLSv1.2_2021"
  }

  tags = {
    Name = "frontend-distribution"
  }

  web_acl_id = data.aws_wafv2_web_acl.cloudfront_managed.arn

  depends_on = [aws_acm_certificate_validation.frontend]
}

# This Web ACL is created and managed by AWS CloudFront
data "aws_wafv2_web_acl" "cloudfront_managed" {
  name  = "CreatedByCloudFront-6bc475d2"
  scope = "CLOUDFRONT"
}

