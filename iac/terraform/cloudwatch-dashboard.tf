# ---------------------------------------------------------------------------------------------
# One page, for the ten minutes after an alarm fires.
#
# This is not a second alarming mechanism and it is not a place to browse. It answers the question
# an alarm email raises and cannot itself answer: an alarm says one metric crossed one line, and
# the next thing anybody wants is whether everything else went with it. A queue backing up while
# errors are flat is a different incident from a queue backing up while the router is throwing.
#
# So the layout is chronological rather than by service -- a request enters at the top and mail
# leaves at the bottom -- and every widget is something an alarm in cloudwatch-alarms.tf can point
# at. Metrics nothing alarms on were left off. A dashboard whose panels nobody has a reason to look
# at is where the panels that matter go to hide.
#
# The first three dashboards in an account are free; this is the account's first.
# ---------------------------------------------------------------------------------------------

locals {
  dashboard_region = data.aws_region.current.region

  # Every function, in the order a request reaches them, rather than the map order of
  # local.lambda_functions. The router and authorizer serve a person who is waiting; the rest run
  # after everybody has been told the work is done, which is exactly why they need looking at.
  dashboard_function_order = [
    aws_lambda_function.giftexchange_app.function_name,
    aws_lambda_function.authorizer.function_name,
    aws_lambda_function.invitation-queue-handler.function_name,
    aws_lambda_function.delivery-events-handler.function_name,
    aws_lambda_function.cooled-off-scheduler-handler.function_name,
  ]

  api_dimensions = {
    ApiName = aws_api_gateway_rest_api.giftexchange-gateway.name
    Stage   = aws_api_gateway_stage.live-stage.stage_name
  }
}

resource "aws_cloudwatch_dashboard" "giftexchange" {
  dashboard_name = "giftexchange"

  dashboard_body = jsonencode({
    widgets = [
      # ----- Row 1: what the caller experienced -----
      {
        type   = "metric"
        x      = 0
        y      = 0
        width  = 12
        height = 6
        properties = {
          title  = "API requests and errors"
          region = local.dashboard_region
          view   = "timeSeries"
          # Stacked, because the useful reading is the proportion of traffic that failed rather
          # than three lines whose relationship has to be worked out each time.
          stacked = true
          period  = 300
          stat    = "Sum"
          metrics = [
            ["AWS/ApiGateway", "Count", "ApiName", local.api_dimensions.ApiName, "Stage", local.api_dimensions.Stage, { label = "requests" }],
            [".", "4XXError", ".", ".", ".", ".", { label = "4xx (client)" }],
            [".", "5XXError", ".", ".", ".", ".", { label = "5xx (server)" }],
          ]
        }
      },
      {
        type   = "metric"
        x      = 12
        y      = 0
        width  = 12
        height = 6
        properties = {
          title   = "API latency, and the ceiling it must stay under"
          region  = local.dashboard_region
          view    = "timeSeries"
          stacked = false
          period  = 300
          metrics = [
            ["AWS/ApiGateway", "Latency", "ApiName", local.api_dimensions.ApiName, "Stage", local.api_dimensions.Stage, { stat = "p50", label = "p50" }],
            ["...", { stat = "p99", label = "p99" }],
            ["AWS/ApiGateway", "IntegrationLatency", "ApiName", local.api_dimensions.ApiName, "Stage", local.api_dimensions.Stage, { stat = "p99", label = "p99 in the function" }],
          ]
          # The two lines that turn this from a graph into a judgement. The lower one is where the
          # alarm fires; the upper is where API Gateway abandons the request and the caller is told
          # a write failed that may well have succeeded -- see the timeout comment in
          # lambda-giftexchange-app.tf.
          annotations = {
            horizontal = [
              { label = "latency alarm", value = 15000, color = "#ff7f0e" },
              { label = "API Gateway integration ceiling", value = 29000, color = "#d62728" },
            ]
          }
          yAxis = { left = { label = "ms", showUnits = false } }
        }
      },

      # ----- Row 2: the functions behind it -----
      {
        type   = "metric"
        x      = 0
        y      = 6
        width  = 12
        height = 6
        properties = {
          title   = "Function errors and throttles"
          region  = local.dashboard_region
          view    = "timeSeries"
          stacked = false
          period  = 300
          stat    = "Sum"
          metrics = concat(
            [for fn in local.dashboard_function_order : ["AWS/Lambda", "Errors", "FunctionName", fn, { label = "${fn} errors" }]],
            [for fn in local.dashboard_function_order : ["AWS/Lambda", "Throttles", "FunctionName", fn, { label = "${fn} throttled" }]]
          )
        }
      },
      {
        type   = "metric"
        x      = 12
        y      = 6
        width  = 12
        height = 6
        properties = {
          title  = "Function duration (p99)"
          region = local.dashboard_region
          view   = "timeSeries"
          # p99 rather than average throughout, for the reason the latency alarm uses it: the cold
          # start is by definition the rare invocation, and an average hides it completely.
          stacked = false
          period  = 300
          metrics = [for fn in local.dashboard_function_order : ["AWS/Lambda", "Duration", "FunctionName", fn, { stat = "p99", label = fn }]]
          yAxis   = { left = { label = "ms", showUnits = false } }
        }
      },

      # ----- Row 3: work that outlives the request -----
      {
        type   = "metric"
        x      = 0
        y      = 12
        width  = 12
        height = 6
        properties = {
          title  = "Queue depth, including anything dead-lettered"
          region = local.dashboard_region
          view   = "timeSeries"
          # Not stacked. A total across these would be meaningless -- one message on a DLQ matters
          # more than fifty in flight on a working queue.
          stacked = false
          period  = 300
          stat    = "Maximum"
          metrics = [
            ["AWS/SQS", "ApproximateNumberOfMessagesVisible", "QueueName", aws_sqs_queue.invitations-queue.name, { label = "invitations waiting" }],
            [".", ".", ".", aws_sqs_queue.delivery-events-queue.name, { label = "delivery events waiting" }],
            [".", ".", ".", aws_sqs_queue.invitations-dlq.name, { label = "invitations DEAD-LETTERED", color = "#d62728" }],
            [".", ".", ".", aws_sqs_queue.delivery-events-dlq.name, { label = "delivery events DEAD-LETTERED", color = "#ff7f0e" }],
          ]
        }
      },
      {
        type   = "metric"
        x      = 12
        y      = 12
        width  = 12
        height = 6
        properties = {
          title   = "How long the oldest invitation has been waiting"
          region  = local.dashboard_region
          view    = "timeSeries"
          stacked = false
          period  = 300
          stat    = "Maximum"
          metrics = [
            ["AWS/SQS", "ApproximateAgeOfOldestMessage", "QueueName", aws_sqs_queue.invitations-queue.name, { label = "invitations" }],
            [".", ".", ".", aws_sqs_queue.delivery-events-queue.name, { label = "delivery events" }],
          ]
          annotations = {
            horizontal = [
              { label = "invitations backing up", value = 900, color = "#d62728" },
            ]
          }
          yAxis = { left = { label = "seconds", showUnits = false } }
        }
      },

      # ----- Row 4: whether the mail is landing -----
      {
        type   = "metric"
        x      = 0
        y      = 18
        width  = 12
        height = 6
        properties = {
          title   = "Mail sent, delivered, and rejected"
          region  = local.dashboard_region
          view    = "timeSeries"
          stacked = false
          period  = 300
          stat    = "Sum"
          metrics = [
            ["AWS/SES", "Send", { label = "sent" }],
            [".", "Delivery", { label = "delivered" }],
            [".", "Bounce", { label = "bounced", color = "#d62728" }],
            [".", "Complaint", { label = "marked as spam", color = "#ff7f0e" }],
          ]
        }
      },
      {
        type   = "metric"
        x      = 12
        y      = 18
        width  = 12
        height = 6
        properties = {
          title  = "Reputation on this application's own sends"
          region = local.dashboard_region
          view   = "timeSeries"
          # Dimensioned to giftexchange-outbound, matching the alarms. The account-wide figures --
          # which include osbornesupremacy.com and silverconcord.com -- are on the landing zone's
          # side, in ahzborn-aws.
          stacked = false
          period  = 3600
          stat    = "Maximum"
          metrics = [
            ["AWS/SES", "Reputation.BounceRate", "ses:configuration-set", data.terraform_remote_state.email.outputs.ses_configuration_set_name, { label = "bounce rate" }],
            [".", "Reputation.ComplaintRate", ".", ".", { label = "complaint rate" }],
          ]
          # Where the alarms sit, and beyond them where AWS starts taking an interest.
          annotations = {
            horizontal = [
              { label = "bounce alarm", value = 0.03, color = "#ff7f0e" },
              { label = "AWS reviews the account", value = 0.05, color = "#d62728" },
            ]
          }
        }
      },
    ]
  })
}
