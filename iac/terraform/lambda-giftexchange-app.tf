resource "aws_lambda_function" "giftexchange_app" {
  function_name    = "giftexchange"
  description      = "Lambda function to handle API Gateway and other requests for the Gift Exchange application"
  handler          = "GiftExchange.Library::GiftExchange.Library.Handlers.Router::FunctionHandler"
  runtime          = "dotnet10"
  architectures    = ["arm64"]
  memory_size      = 128
  timeout          = 30
  filename         = local.publish_zip_path
  source_code_hash = filebase64sha256(local.publish_zip_path)
  role             = aws_iam_role.giftexchange_app_exec_role.arn
  environment {
    variables = merge(
      local.common_environment_variables,
      {
        COOLED_OFF_SCHEDULER_TARGET_ARN = aws_lambda_function.cooled-off-scheduler-handler.arn
        COOLED_OFF_SCHEDULER_ROLE_ARN   = aws_iam_role.cooled-off-scheduler-execution-role.arn
        COOLED_OFF_SCHEDULER_GROUP_NAME = aws_scheduler_schedule_group.cooled-off.name
      }
    )
  }
}

resource "aws_iam_role" "giftexchange_app_exec_role" {
  name = "giftexchange-app-exec-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Principal = {
          Service = "lambda.amazonaws.com"
        }
        Effect = "Allow"
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "giftexchange_app_exec_role_attachment_lambda_basic_execution" {
  role       = aws_iam_role.giftexchange_app_exec_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_lambda_permission" "giftexchange_app_allow_apigw_invoke" {
  statement_id  = "AllowExecutionFromAPIGateway-Invoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.giftexchange_app.arn
  principal     = "apigateway.amazonaws.com"
  source_arn    = "arn:aws:execute-api:${data.aws_region.current.region}:${data.aws_caller_identity.current.account_id}:${aws_api_gateway_rest_api.giftexchange-gateway.id}/*/*"
}

resource "aws_iam_role_policy" "giftexchange_app_dynamodb_policy" {
  name = "giftexchange-app-dynamodb-policy"
  role = aws_iam_role.giftexchange_app_exec_role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "dynamodb:GetItem",
          "dynamodb:PutItem",
          "dynamodb:UpdateItem",
          "dynamodb:DeleteItem",
          "dynamodb:Query",
          "dynamodb:Scan"
        ]
        Resource = [
          aws_dynamodb_table.giftexchange.arn,
          "${aws_dynamodb_table.giftexchange.arn}/index/*" # Allow access to any GSI/LSI on the table
        ]
      }
    ]
  })
}

resource "aws_iam_role_policy" "giftexchange_app_sqs_policy" {
  name = "giftexchange-app-sqs-policy"
  role = aws_iam_role.giftexchange_app_exec_role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "sqs:SendMessage",
          "sqs:GetQueueUrl"
        ]
        Resource = [
          aws_sqs_queue.invitations-queue.arn
        ]
      }
    ]
  })
}

resource "aws_iam_role_policy" "giftexchange_app_comprehend_policy" {
  name = "giftexchange-app-comprehend-policy"
  role = aws_iam_role.giftexchange_app_exec_role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "comprehend:DetectToxicContent"
        ]
        Resource = "*"
      }
    ]
  })
}

resource "aws_iam_role_policy" "giftexchange_app_scheduler_policy" {
  name = "giftexchange-app-scheduler-policy"
  role = aws_iam_role.giftexchange_app_exec_role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "scheduler:CreateSchedule",
          "scheduler:UpdateSchedule"
        ]
        Resource = [
          aws_scheduler_schedule_group.cooled-off.arn,
          "arn:aws:scheduler:${data.aws_region.current.region}:${data.aws_caller_identity.current.account_id}:schedule/${aws_scheduler_schedule_group.cooled-off.name}/*"
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "iam:PassRole"
        ]
        Resource = [
          aws_iam_role.cooled-off-scheduler-execution-role.arn
        ]
      }
    ]
  })
}
