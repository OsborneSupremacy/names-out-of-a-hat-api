# ---------------------------------------------------------------------------------------------
# A participant's invitation on the site: /invitation/{token}, unauthenticated.
#
# Longhand rather than through ./modules/api, for the reason the Ask, /ideas and the leave
# endpoints are: it is reached by clicking a link in an email and it returns a web page.
#
# The link is in the box at the top of a follow-up email, for somebody whose invitation was filed
# under Promotions or spam and who is hearing about the exchange for the first time. The credential
# is a gift ideas token in the path, which already opens an Ask page naming the same pick.
# ---------------------------------------------------------------------------------------------

resource "aws_api_gateway_resource" "invitation-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_rest_api.giftexchange-gateway.root_resource_id
  path_part   = "invitation"
}

resource "aws_api_gateway_resource" "invitation-token-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_resource.invitation-resource.id
  path_part   = "{token}"
}

# GET only. The page shows what the token already entitles its holder to see and changes nothing,
# so mail scanners fetching it on delivery are harmless. The buttons on it lead to /ask, /ideas and
# /offer, which have POSTs of their own.
resource "aws_api_gateway_method" "invitation" {
  rest_api_id   = aws_api_gateway_rest_api.giftexchange-gateway.id
  resource_id   = aws_api_gateway_resource.invitation-token-resource.id
  http_method   = "GET"
  authorization = "NONE"

  request_parameters = {
    "method.request.path.token" = true
  }
}

resource "aws_api_gateway_integration" "invitation" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  resource_id = aws_api_gateway_resource.invitation-token-resource.id
  http_method = aws_api_gateway_method.invitation.http_method

  # POST regardless of the method the caller used: this is how Lambda proxy integrations are
  # invoked, and has nothing to do with the verb the browser sent.
  integration_http_method = "POST"
  type                    = "AWS_PROXY"
  uri                     = aws_lambda_function.giftexchange_app.invoke_arn
  content_handling        = "CONVERT_TO_TEXT"
}

# The same stage-level throttle the leave page has, for the same reason: a token is 256 bits and
# nothing guessed will resolve, but the guessing should cost the guesser more than it costs us.
resource "aws_api_gateway_method_settings" "invitation-throttle" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  stage_name  = aws_api_gateway_stage.live-stage.stage_name
  method_path = "invitation/{token}/GET"

  settings {
    metrics_enabled        = true
    logging_level          = "INFO"
    data_trace_enabled     = false
    throttling_rate_limit  = 5
    throttling_burst_limit = 10
  }

  depends_on = [
    aws_api_gateway_account.this,
    aws_api_gateway_method.invitation
  ]
}
