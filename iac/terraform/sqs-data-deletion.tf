# Requests to delete somebody's data, waiting to be carried out.
#
# Queued because the work has no upper bound. Deleting an organizer's exchanges is one transaction
# per exchange, and somebody with a few years of them can take longer than API Gateway's 29 seconds.
# DeleteMyDataService answers the person as soon as the message is on here.
#
# The visibility timeout matches the handler's timeout, so a message is never handed to a second
# invocation while the first is still deleting.
resource "aws_sqs_queue" "data-deletion-queue" {
  name                       = "giftexchange-data-deletion-queue"
  visibility_timeout_seconds = 300

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.data-deletion-dlq.arn
    # Five, like the delivery events queue and unlike the invitations queue's three. Every step of
    # a deletion is safe to repeat -- a retry finds fewer exchanges and carries on -- so there is
    # nothing a retry can do twice that a person would notice, and a DSQL blip is worth waiting out.
    maxReceiveCount = 5
  })
}

# A request that could not be carried out. Somebody has been told their data is being deleted and it
# has not been, so nothing here is allowed to expire quietly: two weeks, the maximum, and the alarm
# in cloudwatch-alarms.tf is what makes it read.
resource "aws_sqs_queue" "data-deletion-dlq" {
  name = "giftexchange-data-deletion-dlq"

  message_retention_seconds = 1209600
}
