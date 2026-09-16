resource "aws_lambda_function" "data-deletion-queue-handler" {
  function_name = "giftexchange-data-deletion-handler"
  description   = "Function that carries out queued requests to delete somebody's data"
  handler       = "GiftExchange.Library::GiftExchange.Library.Handlers.DataDeletionQueueHandler::FunctionHandler"
  runtime       = "dotnet10"
  architectures = ["arm64"]

  # Sized like the delivery events function, for the same reason: it opens a DSQL connection and
  # signs an IAM token on a cold start, and Lambda scales CPU with memory.
  memory_size = 1024

  # The ceiling that made this a queue in the first place. Matched by the queue's visibility timeout.
  timeout = 300

  filename         = local.publish_zip_path
  source_code_hash = filebase64sha256(local.publish_zip_path)
  role             = aws_iam_role.data-deletion-queue-handler-role.arn

  tracing_config {
    mode = "Active"
  }

  environment {
    variables = local.common_environment_variables
  }
}

# The name is spelled out in deploy-database.yml, which builds this role's ARN for the AWS IAM GRANT
# in db/roles/giftexchange_user--0013.sql. Renaming it here without renaming it there leaves the
# function unable to connect.
resource "aws_iam_role" "data-deletion-queue-handler-role" {
  name = "giftexchange-data-deletion-handler-lambda-role"

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

# No SES permission. Deleting somebody's data sends nobody anything: the participants of an exchange
# being deleted were never told they were in a database, and are not told they have left one.
resource "aws_iam_role_policy" "data-deletion-queue-handler-policy" {
  name = "giftexchange-data-deletion-handler-policy"
  role = aws_iam_role.data-deletion-queue-handler-role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "sqs:ReceiveMessage",
          "sqs:DeleteMessage",
          "sqs:ChangeMessageVisibility",
          "sqs:GetQueueAttributes",
          "sqs:GetQueueUrl"
        ]
        Resource = aws_sqs_queue.data-deletion-queue.arn
      },
      {
        Effect   = "Allow"
        Action   = ["logs:CreateLogGroup", "logs:CreateLogStream", "logs:PutLogEvents"]
        Resource = "arn:aws:logs:*:*:*"
      }
    ]
  })
}

# dsql:DbConnect permits opening a connection; which database role it may open one as is decided by
# the AWS IAM GRANT in db/roles/giftexchange_user--0013.sql.
resource "aws_iam_role_policy" "data-deletion-queue-handler-dsql-policy" {
  name = "giftexchange-data-deletion-handler-dsql-policy"
  role = aws_iam_role.data-deletion-queue-handler-role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["dsql:DbConnect"]
        Resource = [aws_dsql_cluster.giftexchange_dsql_cluster.arn]
      }
    ]
  })
}

# One request at a time, and no more than two at once, for the reason the delivery events mapping
# gives: this is background work, and it must not take the concurrency somebody clicking a button
# needs. Nobody is waiting on it -- they were answered when the message was queued.
resource "aws_lambda_event_source_mapping" "data-deletion-queue-handler-sqs-trigger" {
  event_source_arn = aws_sqs_queue.data-deletion-queue.arn
  function_name    = aws_lambda_function.data-deletion-queue-handler.arn
  batch_size       = 1
  enabled          = true

  scaling_config {
    maximum_concurrency = 2
  }
}
