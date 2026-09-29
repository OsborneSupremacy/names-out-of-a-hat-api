resource "aws_cloudwatch_log_group" "api_gateway_access_logs" {
  name              = "/aws/apigateway/giftexchange-gateway/live-access"
  retention_in_days = 7
}

resource "aws_iam_role" "api_gateway_cloudwatch_role" {
  name = "giftexchange-api-gateway-cloudwatch-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "apigateway.amazonaws.com"
        }
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "api_gateway_cloudwatch_logs" {
  role       = aws_iam_role.api_gateway_cloudwatch_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonAPIGatewayPushToCloudWatchLogs"
}

resource "aws_api_gateway_account" "this" {
  cloudwatch_role_arn = aws_iam_role.api_gateway_cloudwatch_role.arn

  depends_on = [aws_iam_role_policy_attachment.api_gateway_cloudwatch_logs]
}

resource "aws_cloudwatch_log_group" "api_gateway_execution_logs" {
  name              = "API-Gateway-Execution-Logs_${aws_api_gateway_rest_api.giftexchange-gateway.id}/${aws_api_gateway_stage.live-stage.stage_name}"
  retention_in_days = 7
}

resource "aws_api_gateway_method_settings" "all" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  stage_name  = aws_api_gateway_stage.live-stage.stage_name
  method_path = "*/*"

  settings {
    metrics_enabled    = true
    logging_level      = "INFO"
    data_trace_enabled = false

    # A ceiling on the whole stage, for the authenticated routes that have no throttle of their
    # own. Each edit of an exchange or a name costs a Comprehend call, and a session is all it takes
    # to loop one. The web ACL already holds any one IP to a request a second; this bounds what many
    # of them can spend together. The busiest minute in the two weeks before this was set held 49
    # requests, so ten a second with bursts of 25 is well clear of anybody using the site.
    #
    # Shared by everybody, so reaching it throttles real organizers too. That is the point of a
    # ceiling on cost, and the routes with their own settings (auth, ask, ideas, offer, leave,
    # invitation, feedback) keep them.
    throttling_rate_limit  = 10
    throttling_burst_limit = 25
  }

  depends_on = [
    aws_api_gateway_account.this,
    aws_cloudwatch_log_group.api_gateway_execution_logs
  ]
}
