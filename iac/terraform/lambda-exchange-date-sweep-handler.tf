# ---------------------------------------------------------------------------------------------
# The daily sweep over exchange dates: asks organizers to close exchanges a week past their date,
# and deletes exchanges eighteen months past it.
#
# One recurring schedule rather than one per exchange, unlike the cool-off and the delivery check.
# Those are fixed offsets from the send, which never moves; this is measured from a date the
# organizer can edit, and a per-exchange schedule would have to be found and moved every time they
# did. See ExchangeDateSweepService.
# ---------------------------------------------------------------------------------------------

resource "aws_lambda_function" "exchange-date-sweep-handler" {
  function_name = "giftexchange-exchange-date-sweep-handler"
  description   = "Function that prompts organizers to close past exchanges and deletes expired ones"
  handler       = "GiftExchange.Library::GiftExchange.Library.Handlers.ExchangeDateSweepHandler::FunctionHandler"
  runtime       = "dotnet10"
  architectures = ["arm64"]
  memory_size   = 1024
  # Longer than the other scheduled functions, which each handle one exchange. This handles every
  # exchange that is due, one purge transaction at a time, and December is when they pile up.
  timeout          = 300
  filename         = local.publish_zip_path
  source_code_hash = filebase64sha256(local.publish_zip_path)
  role             = aws_iam_role.exchange-date-sweep-handler-role.arn

  tracing_config {
    mode = "Active"
  }

  environment {
    variables = local.common_environment_variables
  }
}

resource "aws_iam_role" "exchange-date-sweep-handler-role" {
  name = "giftexchange-exchange-date-sweep-handler-lambda-role"

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

resource "aws_iam_role_policy" "exchange-date-sweep-handler-policy" {
  name = "giftexchange-exchange-date-sweep-handler-policy"
  role = aws_iam_role.exchange-date-sweep-handler-role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      # It reads hats across every organizer, marks close prompts sent, and deletes expired
      # exchanges. It connects as giftexchange_user, so it also needs the database-side mapping in
      # db/roles/giftexchange_user--0018.sql.
      {
        Effect = "Allow"
        Action = [
          "dsql:DbConnect"
        ]
        Resource = [
          aws_dsql_cluster.giftexchange_dsql_cluster.arn
        ]
      },
      # SendRawEmail alone, for the same reason as the undeliverable-invitations function: the
      # close prompt goes to one organizer through AutomaticEmailSender.
      {
        Effect = "Allow"
        Action = [
          "ses:SendRawEmail"
        ]
        Resource = "*"
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

resource "aws_iam_role" "exchange-date-sweep-scheduler-execution-role" {
  name = "giftexchange-exchange-date-sweep-scheduler-execution-role"

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

resource "aws_iam_role_policy" "exchange-date-sweep-scheduler-execution-policy" {
  name = "giftexchange-exchange-date-sweep-scheduler-execution-policy"
  role = aws_iam_role.exchange-date-sweep-scheduler-execution-role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "lambda:InvokeFunction"
        ]
        Resource = [
          aws_lambda_function.exchange-date-sweep-handler.arn
        ]
      }
    ]
  })
}

resource "aws_scheduler_schedule" "exchange-date-sweep" {
  name        = "giftexchange-exchange-date-sweep"
  description = "Daily close prompts and retention purge, measured from each exchange's date"

  # 15:00 UTC is mid-morning in the Americas and afternoon in Europe, so a close prompt lands
  # during somebody's day rather than overnight.
  schedule_expression          = "cron(0 15 * * ? *)"
  schedule_expression_timezone = "UTC"

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = aws_lambda_function.exchange-date-sweep-handler.arn
    role_arn = aws_iam_role.exchange-date-sweep-scheduler-execution-role.arn
    input    = jsonencode({})

    # The function catches everything itself, so a retry would only ever follow a failed invoke --
    # a throttle, say. A couple are worth having; tomorrow's run covers anything beyond that.
    retry_policy {
      maximum_retry_attempts       = 2
      maximum_event_age_in_seconds = 3600
    }
  }
}
