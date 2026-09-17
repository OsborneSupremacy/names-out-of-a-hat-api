# ---------------------------------------------------------------------------------------------
# Sharing gift ideas: /ideas/{token}, unauthenticated.
#
# Longhand rather than through ./modules/api, for the reason the Ask and the leave endpoints are:
# that module assumes an authenticated endpoint carrying JSON request and response models, and
# these two are neither. They are reached by clicking a SHARE GIFT IDEAS button in an email and
# they return a web page.
#
# The credential is the token in the path. It is the same token the Ask uses for a participant's
# own ideas, or an ask's token for ideas about somebody else.
# ---------------------------------------------------------------------------------------------

resource "aws_api_gateway_resource" "ideas-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_rest_api.giftexchange-gateway.root_resource_id
  path_part   = "ideas"
}

resource "aws_api_gateway_resource" "ideas-token-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_resource.ideas-resource.id
  path_part   = "{token}"
}

locals {
  # GET renders the form; POST behind the button on it stores and forwards. Split, like the Ask,
  # because mail security scanners fetch links in delivered mail, and fetching this link must only
  # ever show a page.
  ideas_methods = ["GET", "POST"]
}

resource "aws_api_gateway_method" "ideas" {
  for_each = toset(local.ideas_methods)

  rest_api_id   = aws_api_gateway_rest_api.giftexchange-gateway.id
  resource_id   = aws_api_gateway_resource.ideas-token-resource.id
  http_method   = each.value
  authorization = "NONE"

  request_parameters = {
    "method.request.path.token" = true
  }
}

resource "aws_api_gateway_integration" "ideas" {
  for_each = toset(local.ideas_methods)

  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  resource_id = aws_api_gateway_resource.ideas-token-resource.id
  http_method = aws_api_gateway_method.ideas[each.value].http_method

  # POST regardless of the method the caller used: this is how Lambda proxy integrations are
  # invoked, and has nothing to do with the verb the browser sent.
  integration_http_method = "POST"
  type                    = "AWS_PROXY"
  uri                     = aws_lambda_function.giftexchange_app.invoke_arn
  content_handling        = "CONVERT_TO_TEXT"
}

# No OPTIONS response, for the reason the Ask has none: a browser navigates here and the form on the
# page posts back. Nothing calls these from JavaScript.

# Nothing in the application throttles a submission, so this is the only limit. It is there for
# somebody enumerating tokens, who never resolves one; a participant sharing ideas sends a handful of
# requests at most.
resource "aws_api_gateway_method_settings" "ideas-throttle" {
  for_each = toset(local.ideas_methods)

  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  stage_name  = aws_api_gateway_stage.live-stage.stage_name
  method_path = "ideas/{token}/${each.value}"

  settings {
    metrics_enabled        = true
    logging_level          = "INFO"
    data_trace_enabled     = false
    throttling_rate_limit  = 5
    throttling_burst_limit = 10
  }

  depends_on = [
    aws_api_gateway_account.this,
    aws_api_gateway_method.ideas
  ]
}
