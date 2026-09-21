# ---------------------------------------------------------------------------------------------
# Offering gift ideas about another participant: /offer/{token}, unauthenticated.
#
# Longhand rather than through ./modules/api, for the reason /ideas, the Ask and the leave
# endpoints are: that module assumes an authenticated endpoint carrying JSON request and response
# models, and these are none of those. They are reached by clicking a button in an email and they
# return a web page.
#
# The credential is the token in the path, and it is the same token /ideas uses for a participant's
# own ideas. A resource of its own rather than another method on /ideas because the subject is
# chosen on the page here rather than settled by the link -- see OfferGiftIdeasService.
# ---------------------------------------------------------------------------------------------

resource "aws_api_gateway_resource" "offer-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_rest_api.giftexchange-gateway.root_resource_id
  path_part   = "offer"
}

resource "aws_api_gateway_resource" "offer-token-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_resource.offer-resource.id
  path_part   = "{token}"
}

locals {
  # GET renders the form; POST behind the button on it stores and sends. Split, like /ideas and the
  # Ask, because mail security scanners fetch links in delivered mail, and fetching this link must
  # only ever show a page.
  offer_methods = ["GET", "POST"]
}

resource "aws_api_gateway_method" "offer" {
  for_each = toset(local.offer_methods)

  rest_api_id   = aws_api_gateway_rest_api.giftexchange-gateway.id
  resource_id   = aws_api_gateway_resource.offer-token-resource.id
  http_method   = each.value
  authorization = "NONE"

  request_parameters = {
    "method.request.path.token" = true
  }
}

resource "aws_api_gateway_integration" "offer" {
  for_each = toset(local.offer_methods)

  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  resource_id = aws_api_gateway_resource.offer-token-resource.id
  http_method = aws_api_gateway_method.offer[each.value].http_method

  # POST regardless of the method the caller used: this is how Lambda proxy integrations are
  # invoked, and has nothing to do with the verb the browser sent.
  integration_http_method = "POST"
  type                    = "AWS_PROXY"
  uri                     = aws_lambda_function.giftexchange_app.invoke_arn
  content_handling        = "CONVERT_TO_TEXT"
}

# No OPTIONS response, for the reason /ideas has none: a browser navigates here and the form on the
# page posts back. Nothing calls these from JavaScript.

# The application throttles an offer per sender and subject for a week, so unlike /ideas this is not
# the only limit -- but it is the only one that applies before a token has been resolved, which is
# the case it is here for: somebody enumerating tokens never resolves one.
resource "aws_api_gateway_method_settings" "offer-throttle" {
  for_each = toset(local.offer_methods)

  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  stage_name  = aws_api_gateway_stage.live-stage.stage_name
  method_path = "offer/{token}/${each.value}"

  settings {
    metrics_enabled        = true
    logging_level          = "INFO"
    data_trace_enabled     = false
    throttling_rate_limit  = 5
    throttling_burst_limit = 10
  }

  depends_on = [
    aws_api_gateway_account.this,
    aws_api_gateway_method.offer
  ]
}
