resource "aws_scheduler_schedule_group" "cooled-off" {
  name = local.cooled_off_scheduler_group_name
}

resource "aws_lambda_function" "cooled-off-scheduler-handler" {
  function_name    = "giftexchange-cooled-off-scheduler-handler"
  description      = "Function that transitions hats from INVITATIONS_SENT to READY_TO_CLOSE"
  handler          = "GiftExchange.Library::GiftExchange.Library.Handlers.CooledOffSchedulerHandler::FunctionHandler"
  runtime          = "dotnet10"
  architectures    = ["arm64"]
  memory_size      = 128
  timeout          = 30
  filename         = local.publish_zip_path
  source_code_hash = filebase64sha256(local.publish_zip_path)
  role             = aws_iam_role.cooled-off-scheduler-handler-role.arn

  environment {
    variables = local.common_environment_variables
  }
}

resource "aws_iam_role" "cooled-off-scheduler-handler-role" {
  name = "giftexchange-cooled-off-scheduler-handler-lambda-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Principal = {
          Service = "lambda.amazonaws.com"
        }
        Action = "sts:AssumeRole"
      }
    ]
  })
}

resource "aws_iam_role_policy" "cooled-off-scheduler-handler-policy" {
  name = "giftexchange-cooled-off-scheduler-handler-policy"
  role = aws_iam_role.cooled-off-scheduler-handler-role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "dynamodb:UpdateItem"
        ]
        Resource = [
          aws_dynamodb_table.giftexchange.arn
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "logs:CreateLogGroup",
          "logs:CreateLogStream",
          "logs:PutLogEvents"
        ]
        Resource = "arn:aws:logs:*:*:*"
      }
    ]
  })
}

resource "aws_iam_role" "cooled-off-scheduler-execution-role" {
  name = "giftexchange-cooled-off-scheduler-execution-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Principal = {
          Service = "scheduler.amazonaws.com"
        }
        Action = "sts:AssumeRole"
      }
    ]
  })
}

resource "aws_iam_role_policy" "cooled-off-scheduler-execution-policy" {
  name = "giftexchange-cooled-off-scheduler-execution-policy"
  role = aws_iam_role.cooled-off-scheduler-execution-role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "lambda:InvokeFunction"
        ]
        Resource = [
          aws_lambda_function.cooled-off-scheduler-handler.arn
        ]
      }
    ]
  })
}

resource "aws_lambda_permission" "cooled-off-scheduler-handler-allow-scheduler-invoke" {
  statement_id  = "AllowExecutionFromEventBridgeScheduler"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.cooled-off-scheduler-handler.arn
  principal     = "scheduler.amazonaws.com"
  source_arn    = "arn:aws:scheduler:${data.aws_region.current.region}:${data.aws_caller_identity.current.account_id}:schedule/${aws_scheduler_schedule_group.cooled-off.name}/*"
}
