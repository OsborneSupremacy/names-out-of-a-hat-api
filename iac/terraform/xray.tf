# ---------------------------------------------------------------------------------------------
# X-Ray tracing.
#
# What this buys, stated precisely, because the gap between the two halves matters.
#
# Active tracing on a function, plus tracing on the API Gateway stage, gives a trace per request
# with the gateway and the function on it: how long API Gateway held the request, how much of that
# was the integration, and -- the part nothing else reports -- the Init phase separately from the
# invocation. That last one is the number every performance comment in this repository is really
# about. lambda-giftexchange-app.tf explains that a cold start at 128 MB was exceeding the 29
# second ceiling and that memory was raised to fix it, and the way that was established was by
# inference from failures. This measures it directly, and it distinguishes a slow request from a
# request that was merely behind a slow cold start.
#
# What it does NOT give is the breakdown inside the invocation. The subsegments that would say how
# much of a cold start is EF model building versus opening a DSQL connection versus signing an IAM
# token require the X-Ray SDK registered against the AWS SDK clients in ServiceProviderBuilder,
# which is a code change and is not in this one. The infrastructure here is what that change would
# need in place first, and it is independently useful without it -- but it is worth being clear
# that this alone will not point at a slow query.
#
# Cost is not a consideration at this volume. The first 100,000 traces recorded per month are free
# and this application will not approach that, holiday season included.
#
# Applied to all five functions rather than the API router alone. The router is where latency is
# user-visible, but the queue handlers are where the work happens unobserved, and those are the
# ones where "it ran and something took a while" is currently the entire available account.
# ---------------------------------------------------------------------------------------------

resource "aws_iam_role_policy_attachment" "xray_write" {
  for_each = {
    router               = aws_iam_role.giftexchange_app_exec_role.name
    authorizer           = aws_iam_role.authorizer_exec_role.name
    invitation_queue     = aws_iam_role.invitation-queue-handler-role.name
    delivery_events      = aws_iam_role.delivery-events-handler-role.name
    cooled_off_scheduler = aws_iam_role.cooled-off-scheduler-handler-role.name
  }

  role = each.value

  # The managed policy rather than an inline one. It grants exactly xray:PutTraceSegments,
  # xray:PutTelemetryRecords and the two sampling rule reads, all of which are write-only or
  # read-nothing-sensitive, and there is no narrower version of it to write -- the resource for
  # these actions can only be "*", because a trace segment has no ARN to scope to.
  policy_arn = "arn:aws:iam::aws:policy/AWSXRayDaemonWriteAccess"
}
