# ---------------------------------------------------------------------------------------------
# The eight Lambda log groups, and why they are declared at all.
#
# A log group nobody creates still exists. Lambda makes one on the function's first invocation,
# and a group created that way has no retention policy -- which does not mean a default, it means
# never expire. So the choice was never between keeping logs and not keeping them; it was between
# a retention period somebody picked and one nobody did.
#
# That matters more here than it would in most applications, because these logs contain
# participant email addresses (InvitationQueueHandlerService logs the recipient of every send).
# Keeping those forever is a decision, and it was not one anybody had made.
#
# Naming is not a choice: Lambda writes to /aws/lambda/<function name> and cannot be told to write
# anywhere else, so these have to match exactly or the function creates its own alongside them and
# nothing here applies to the logs that actually exist.
# ---------------------------------------------------------------------------------------------

locals {
  # Thirty days. Long enough to still be reading the season's logs in January, when the questions
  # about what happened in December get asked; short enough that a participant's address is not
  # held for years because of a debugging line. If the PII in these is ever removed at the source,
  # this can go up rather than down -- but retention is the cheaper of the two fixes and it is the
  # one that works on logs already written.
  lambda_log_retention_days = 30

  # Every function that writes logs, keyed by the log group suffix. Used again in
  # cloudwatch-alarms.tf, which is the point: a function added here without an alarm, or alarmed
  # without a log group, is the kind of gap that is only ever noticed when it matters.
  lambda_functions = {
    (aws_lambda_function.giftexchange_app.function_name)                  = "API router"
    (aws_lambda_function.authorizer.function_name)                        = "session authorizer"
    (aws_lambda_function.invitation-queue-handler.function_name)          = "invitation sender"
    (aws_lambda_function.delivery-events-handler.function_name)           = "SES delivery events"
    (aws_lambda_function.cooled-off-scheduler-handler.function_name)      = "cool-off transition"
    (aws_lambda_function.undeliverable-invitations-handler.function_name) = "undeliverable invitations"
    (aws_lambda_function.data-deletion-queue-handler.function_name)       = "data deletion"
    (aws_lambda_function.exchange-date-sweep-handler.function_name)       = "exchange date sweep"
  }
}

resource "aws_cloudwatch_log_group" "lambda" {
  for_each = local.lambda_functions

  name              = "/aws/lambda/${each.key}"
  retention_in_days = local.lambda_log_retention_days
}
