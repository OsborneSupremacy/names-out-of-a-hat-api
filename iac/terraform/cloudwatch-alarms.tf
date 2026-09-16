# ---------------------------------------------------------------------------------------------
# The alarms the topic in sns-notifications.tf was created for.
#
# Two rules shape everything below.
#
# The first is that every alarm here has to be worth waking up for. This application has one
# operator and one inbox, and an alarm that fires on something nobody would act on does not just
# waste a notification -- it teaches the reader to skim the next one. So there are no alarms on
# 4XX (a client sending bad requests is the client's problem), none on invocation counts, and none
# on duration except at the ceiling where duration turns into failure.
#
# The second is that missing data is not a problem. Most of these watch things that are normally
# absent: an empty DLQ publishes nothing to alarm on, and a function nobody has invoked reports no
# errors because there were none. Treating that as breaching would mean an inbox full of alarms
# every quiet week in July, so it is treated as fine nearly everywhere -- and where it is not, the
# comment says why.
#
# ok_actions is set on all of them. A recovery notice costs one more email and is the difference
# between knowing an incident ended and assuming it did.
# ---------------------------------------------------------------------------------------------

# ------------------------------- Lambda -------------------------------

# Any unhandled exception in any function. Deliberately a threshold of zero rather than a rate:
# at this volume a single error is a real one, and a percentage would need traffic to be
# meaningful -- which is precisely what a newly promoted application does not have yet.
resource "aws_cloudwatch_metric_alarm" "lambda_errors" {
  for_each = local.lambda_functions

  alarm_name        = "giftexchange-${each.key}-errors"
  alarm_description = "Unhandled errors in the ${each.value} function."

  namespace   = "AWS/Lambda"
  metric_name = "Errors"
  dimensions  = { FunctionName = each.key }

  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 0
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

# Concurrency denied, which is the failure that has actually happened here. The incident recorded
# in lambda-delivery-events-handler.tf -- a fan-out of delivery events taking the whole account's
# concurrency and leaving the API function with none -- was invisible while it was happening and
# was diagnosed afterwards from a user-visible 500. The reserved concurrency added then stops it
# recurring in that shape; this is what says so if it recurs in another.
resource "aws_cloudwatch_metric_alarm" "lambda_throttles" {
  for_each = local.lambda_functions

  alarm_name        = "giftexchange-${each.key}-throttles"
  alarm_description = "The ${each.value} function was denied a concurrency slot."

  namespace   = "AWS/Lambda"
  metric_name = "Throttles"
  dimensions  = { FunctionName = each.key }

  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 0
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

# ------------------------------- API Gateway -------------------------------

# Errors the caller saw. This overlaps with the Lambda errors alarm and is kept anyway: a 5XX can
# come from the integration timing out, the authorizer failing, or API Gateway itself, none of
# which register as a Lambda error. Two alarms firing together identifies the fault faster than
# either firing alone, and one firing without the other is itself the diagnosis.
resource "aws_cloudwatch_metric_alarm" "api_5xx" {
  alarm_name        = "giftexchange-api-5xx"
  alarm_description = "The API returned server errors to callers."

  namespace   = "AWS/ApiGateway"
  metric_name = "5XXError"
  dimensions = {
    ApiName = aws_api_gateway_rest_api.giftexchange-gateway.name
    Stage   = aws_api_gateway_stage.live-stage.stage_name
  }

  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 0
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

# The approach to the ceiling, rather than the crash into it.
#
# lambda-giftexchange-app.tf sets the function timeout to 28 seconds to stay under API Gateway's
# 29 second integration limit, and explains what went wrong when the two disagreed. That comment
# describes a cliff. Nothing measured how close the application was running to it.
#
# Fifteen seconds at p99 is not a failure and no caller has been turned away at that point -- it is
# the warning that a cold start on a cold DSQL connection is now taking half the budget it has, at
# which point there is still time to raise memory or reconsider before the season makes it worse.
# p99 rather than average because the cold start is by definition the rare request; an average
# would be dominated by warm invocations and would sit flat right up until the cliff.
resource "aws_cloudwatch_metric_alarm" "api_latency" {
  alarm_name        = "giftexchange-api-latency"
  alarm_description = "p99 request latency is approaching the 29 second API Gateway integration ceiling."

  namespace   = "AWS/ApiGateway"
  metric_name = "Latency"
  dimensions = {
    ApiName = aws_api_gateway_rest_api.giftexchange-gateway.name
    Stage   = aws_api_gateway_stage.live-stage.stage_name
  }

  extended_statistic = "p99"
  period             = 300

  # Two periods, unlike everything else here. A single slow cold start after an idle hour is
  # normal and is not news; ten minutes of them is a trend.
  evaluation_periods  = 2
  threshold           = 15000
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

# ------------------------------- Queues -------------------------------

# A message in either dead letter queue.
#
# Both DLQs were built to make an unprocessable message visible rather than let it be retried into
# oblivion, and neither was visible to anybody: nothing consumes them and nothing reported them, so
# "visible" meant visible to a person who thought to open the SQS console. This is the part that
# was missing. One message is the threshold because one message is one participant.
resource "aws_cloudwatch_metric_alarm" "dlq_not_empty" {
  for_each = {
    (aws_sqs_queue.delivery-events-dlq.name) = "SES delivery event"
    (aws_sqs_queue.invitations-dlq.name)     = "invitation"
    (aws_sqs_queue.data-deletion-dlq.name)   = "data deletion"
  }

  alarm_name        = "giftexchange-${each.key}-not-empty"
  alarm_description = "An ${each.value} message could not be processed and has been dead-lettered. The payload is on the queue."

  namespace   = "AWS/SQS"
  metric_name = "ApproximateNumberOfMessagesVisible"
  dimensions  = { QueueName = each.key }

  statistic = "Maximum"
  period    = 300

  evaluation_periods  = 1
  threshold           = 0
  comparison_operator = "GreaterThanThreshold"

  # SQS stops publishing metrics for a queue left untouched for six hours, so an empty DLQ -- the
  # normal case -- eventually reports nothing at all rather than reporting zero. Alarming on that
  # absence would mean an alarm every quiet day.
  treat_missing_data = "notBreaching"

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

# Invitations queued but not going out.
#
# The worst failure this application has, because it is silent in both directions: the organizer
# has been told invitations were sent, and the participants are simply waiting. Nothing on either
# screen distinguishes it from mail in flight.
#
# Fifteen minutes is well past normal -- the mapping batches for at most thirty seconds and the
# send itself is one SES call -- and well short of the point where an organizer starts asking
# people whether they got it.
resource "aws_cloudwatch_metric_alarm" "invitations_backing_up" {
  alarm_name        = "giftexchange-invitations-backing-up"
  alarm_description = "Invitations have been sitting on the queue unsent. Participants are waiting and the organizer has been told they were sent."

  namespace   = "AWS/SQS"
  metric_name = "ApproximateAgeOfOldestMessage"
  dimensions  = { QueueName = aws_sqs_queue.invitations-queue.name }

  statistic           = "Maximum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 900
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

# ------------------------------- SES reputation -------------------------------

# What this domain's own recipients are doing with its mail.
#
# Dimensioned by configuration set, not account-wide, and the distinction is the whole point.
# Three domains send from this AWS account -- osbornesupremacy.com and silverconcord.com are
# declared in the ahzborn-aws landing zone and share default-ses-configuration-set. An undimensioned
# Reputation.BounceRate is the average across all three, so an alarm on it fires here for a bad
# batch sent from somewhere else, and dilutes this application's own bounces in somebody else's
# volume in the other direction. It answers a question about the account while wearing the name of
# the application.
#
# The account-wide version of these two is worth having and lives in ahzborn-aws, next to the
# identities and the quota it actually describes.
#
# email/terraform already enables reputation metrics on giftexchange-outbound, for the reason
# written at ses-delivery-events.tf:20 -- one organizer typing addresses badly is everybody else's
# deliverability, and a rate is the only view that shows that building. These are the alarms that
# comment was anticipating; the metric has been published all along with nothing reading it.
#
# Thresholds sit below the points at which AWS acts (5% bounces, 0.1% complaints) rather than at
# them, since an alarm arriving with the review has told you nothing you can still act on. They are
# per-configuration-set here, so crossing one is not yet an AWS matter at all -- it is the earliest
# point at which this application can see itself heading there.
#
# These are also the alarms most likely to fire for a reason that is nobody's fault: a batch of
# addresses typed from a family group chat will contain typos, and typos bounce. That is exactly
# when it is worth knowing, because the remedy -- correcting the address, which
# api-edit-participant-address.tf exists to make possible -- is available for a few days and then
# is not.

locals {
  # The colon in the key is not a typo. SES publishes these under the dimension name
  # "ses:configuration-set", which is unusual enough that a plausible-looking "ConfigurationSet"
  # would produce an alarm that is syntactically fine, never matches a metric, and sits in
  # INSUFFICIENT_DATA forever looking healthy enough to ignore.
  ses_configuration_set_dimension = {
    "ses:configuration-set" = data.terraform_remote_state.email.outputs.ses_configuration_set_name
  }
}

resource "aws_cloudwatch_metric_alarm" "ses_bounce_rate" {
  alarm_name        = "giftexchange-ses-bounce-rate"
  alarm_description = "Bounce rate on this application's own sends is climbing toward the level at which AWS reviews the account."

  namespace   = "AWS/SES"
  metric_name = "Reputation.BounceRate"
  dimensions  = local.ses_configuration_set_dimension

  statistic = "Maximum"

  # An hour, because that is roughly how often SES recalculates these. A five minute period would
  # mostly evaluate the same number repeatedly and report missing data in between.
  period              = 3600
  evaluation_periods  = 1
  threshold           = 0.03
  comparison_operator = "GreaterThanThreshold"

  # Hold the previous state rather than assume health. Unlike the queues, an absent reputation
  # figure is not evidence of anything good, and this is the one alarm here where quietly deciding
  # that no news is good news would be wrong.
  treat_missing_data = "missing"

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "ses_complaint_rate" {
  alarm_name        = "giftexchange-ses-complaint-rate"
  alarm_description = "Complaint rate on this application's own sends is climbing toward the level at which AWS reviews the account."

  namespace   = "AWS/SES"
  metric_name = "Reputation.ComplaintRate"
  dimensions  = local.ses_configuration_set_dimension

  statistic           = "Maximum"
  period              = 3600
  evaluation_periods  = 1
  threshold           = 0.0005
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "missing"

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}
