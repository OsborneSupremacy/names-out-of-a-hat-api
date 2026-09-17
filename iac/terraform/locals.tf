locals {
  cooled_off_scheduler_group_name = "giftexchange-cooled-off"

  # Kept apart from the cool-off group on purpose. Both hold one-shot schedules created by
  # the same send, but they fire hours apart for unrelated reasons, and a single group would
  # make either one impossible to count or clear without disturbing the other.
  undeliverable_scheduler_group_name = "giftexchange-undeliverable-invitations"

  # The topic SES publishes delivery events to. Declared in email/terraform alongside the rest of
  # this domain's sending configuration, and reached through the terraform_remote_state in data.tf.
  delivery_events_topic_arn = data.terraform_remote_state.email.outputs.delivery_events_topic_arn

  common_environment_variables = {
    "TABLE_NAME"                    = aws_dynamodb_table.giftexchange.name,
    "INVITATIONS_QUEUE_URL"         = aws_sqs_queue.invitations-queue.url,
    "CONTENT_MODERATION_THRESHOLD"  = "0.5" // higher is _less_ sensitive
    "SESSION_SIGNING_KEY_PARAMETER" = aws_ssm_parameter.session_signing_key.name
    # Whether mail goes to the address it is addressed to, or is diverted to the test recipient
    # with " - TEST MODE" appended to the subject.
    #
    # Common rather than per-function, because it was per-function and that went wrong: three
    # services read it, two of the functions carrying them set it, and the one that did not was the
    # router — so magic link sign-in emails went out marked TEST MODE while invitations from the
    # queue handler went out correctly. Nothing failed and nothing logged an error; the flag simply
    # read false wherever it had not been set. Anything that has to be remembered separately for
    # each function will eventually be forgotten for one of them.
    "LIVE_MODE" = "true"
    # The cluster endpoint, not the CNAME. DSQL's certificate covers *.dsql.<region>.on.aws,
    # and the connector always uses verify-full, so connecting via giftexchange-db.namesoutofahat.com
    # would fail hostname verification.
    "DSQL_ENDPOINT" = "${aws_dsql_cluster.giftexchange_dsql_cluster.identifier}.dsql.${data.aws_region.current.region}.on.aws"
    # Named on every participant send, and the only thing that makes SES report what became of it.
    # Common rather than per-function for the reason LIVE_MODE is: a variable that has to be
    # remembered separately for each function is one that will eventually be forgotten for one of
    # them, and forgetting this one fails silently -- the mail still sends, and nothing is heard
    # back about it ever again.
    "SES_CONFIGURATION_SET" = data.terraform_remote_state.email.outputs.ses_configuration_set_name
    # Where the contact form's messages go. Deliberately not the alarms topic -- see
    # sns-notifications.tf for why the two are kept apart.
    "FEEDBACK_TOPIC_ARN" = aws_sns_topic.feedback.arn
    # What the X-Ray SDK does when asked to record a subsegment with no segment to hang it on.
    #
    # The default is to throw, which would turn "this trace is incomplete" into "this request
    # failed" -- an observability tool taking down the thing it observes. Nothing should hit this:
    # ServiceProviderBuilder only registers the AWS SDK handler inside Lambda, where the runtime
    # provides a facade segment. But the cost of being wrong about that is a 500 to an organizer,
    # and the cost of this line is a log entry.
    "AWS_XRAY_CONTEXT_MISSING" = "LOG_ERROR"

    # Powertools names the segment and, when Logging and Metrics are added later, the log field and
    # metric dimension too. Left unset it defaults to the literal "service_undefined", which is the
    # kind of value that goes unnoticed until somebody is looking at a trace and cannot tell which
    # application produced it.
    "POWERTOOLS_SERVICE_NAME" = "giftexchange"

    # The default is true, and true would put every API response this application returns into
    # trace metadata -- participants, addresses, and for a closed exchange the assignments
    # themselves. Each handler already passes CaptureMode explicitly; this is the backstop for the
    # one that eventually will not, because a default that leaks is worse than no default at all.
    "POWERTOOLS_TRACER_CAPTURE_RESPONSE" = "false"

    # Errors stay on. An exception is the thing a trace is most often opened to read, and unlike a
    # response it is not a wholesale copy of somebody's data.
    "POWERTOOLS_TRACER_CAPTURE_ERROR" = "true"
  }
  publish_zip_path = "../../src/GiftExchange.Library/bin/GiftExchange.Library.zip"
}
