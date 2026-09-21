resource "aws_lambda_function" "giftexchange_app" {
  function_name = "giftexchange"
  description   = "Lambda function to handle API Gateway and other requests for the Gift Exchange application"
  handler       = "GiftExchange.Library::GiftExchange.Library.Handlers.Router::FunctionHandler"
  runtime       = "dotnet10"
  architectures = ["arm64"]
  # Lambda scales CPU with memory, so this is a CPU setting as much as a memory one. At 128 MB
  # a cold start has to build the EF model, open a DSQL connection and sign an IAM token on a
  # fraction of a core, which was exceeding API Gateway's 29 second integration ceiling. More
  # memory usually costs the same or less here, because the work finishes in far fewer
  # GB-seconds.
  memory_size = 1024

  # Below API Gateway's 29 second integration ceiling, deliberately.
  #
  # At 30 seconds the two disagreed about who gives up first: API Gateway abandoned the request
  # and returned 504 while the function carried on and could still commit, so a client could be
  # told the write failed after it had succeeded. Timing out first means the invocation is killed,
  # the transaction rolls back, and a failure the caller sees is a failure that happened. One
  # second under the ceiling rather than exactly on it, because API Gateway starts its clock
  # slightly before the invocation does.
  timeout          = 28
  filename         = local.publish_zip_path
  source_code_hash = filebase64sha256(local.publish_zip_path)
  role             = aws_iam_role.giftexchange_app_exec_role.arn
  # Traces the gateway hop and, separately, the Init phase this function's memory setting is
  # sized for. See xray.tf for what that does and does not show without SDK instrumentation.
  tracing_config {
    mode = "Active"
  }

  environment {
    variables = merge(
      local.common_environment_variables,
      {
        COOLED_OFF_SCHEDULER_TARGET_ARN = aws_lambda_function.cooled-off-scheduler-handler.arn
        COOLED_OFF_SCHEDULER_ROLE_ARN   = aws_iam_role.cooled-off-scheduler-execution-role.arn
        COOLED_OFF_SCHEDULER_GROUP_NAME = aws_scheduler_schedule_group.cooled-off.name

        # The second schedule the send path creates. Only the router builds schedules, so these
        # stay here rather than in common_environment_variables -- unlike LIVE_MODE, a function
        # that does not have them is a function that never wanted them, and SchedulerService is
        # only ever constructed where one is about to be made.
        UNDELIVERABLE_SCHEDULER_TARGET_ARN = aws_lambda_function.undeliverable-invitations-handler.arn
        UNDELIVERABLE_SCHEDULER_ROLE_ARN   = aws_iam_role.undeliverable-invitations-scheduler-execution-role.arn
        UNDELIVERABLE_SCHEDULER_GROUP_NAME = aws_scheduler_schedule_group.undeliverable-invitations.name

        # Only the router queues data deletions; the function that carries them out reads the queue
        # through its event source mapping and never needs the URL.
        DATA_DELETION_QUEUE_URL = aws_sqs_queue.data-deletion-queue.url
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

# The table holds only magic link tokens now that gift exchanges live in DSQL. LoginTokenProvider
# writes a token and consumes it with a conditional DeleteItem that returns the old item, so those
# two actions cover everything it does.
#
# Scan is withheld deliberately. Only the hash of a token is stored, precisely so that reading the
# table does not yield anything replayable; being able to enumerate it would undo that. There are
# no indexes either, so the ARN needs no /index/* suffix.
resource "aws_iam_role_policy" "giftexchange_app_dynamodb_policy" {
  name = "giftexchange-app-dynamodb-policy"
  role = aws_iam_role.giftexchange_app_exec_role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "dynamodb:PutItem",
          "dynamodb:DeleteItem"
        ]
        Resource = [aws_dynamodb_table.giftexchange.arn]
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
          aws_sqs_queue.invitations-queue.arn,
          aws_sqs_queue.data-deletion-queue.arn
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
        # Both groups the send path writes to. The undeliverable-invitations group was left out
        # when it was added, and every send from then on failed to schedule its delivery check
        # without failing the send, so nothing but the log noticed.
        Resource = [
          aws_scheduler_schedule_group.cooled-off.arn,
          "arn:aws:scheduler:${data.aws_region.current.region}:${data.aws_caller_identity.current.account_id}:schedule/${aws_scheduler_schedule_group.cooled-off.name}/*",
          aws_scheduler_schedule_group.undeliverable-invitations.arn,
          "arn:aws:scheduler:${data.aws_region.current.region}:${data.aws_caller_identity.current.account_id}:schedule/${aws_scheduler_schedule_group.undeliverable-invitations.name}/*"
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "iam:PassRole"
        ]
        Resource = [
          aws_iam_role.cooled-off-scheduler-execution-role.arn,
          aws_iam_role.undeliverable-invitations-scheduler-execution-role.arn
        ]
      }
    ]
  })
}
