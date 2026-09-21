namespace GiftExchange.Library.Messaging;

/// <summary>
/// One SES event notification, as it arrives on the delivery events queue.
///
/// Only the parts this application reads are modelled. SES publishes a good deal more — the whole
/// header set, per-recipient detail, the sending IP — and adding a property here is the only thing
/// needed to start reading any of it.
/// </summary>
/// <remarks>
/// Nothing here is <c>required</c>, and nothing here is non-nullable without a default. This is a
/// wire format belonging to somebody else: a field this application has decided is mandatory turns
/// an unfamiliar event into a deserialization failure, which the queue then redelivers until it
/// gives up. Missing values are handled where they are read instead, where there is something
/// useful to do about them.
/// </remarks>
internal record SesDeliveryEvent
{
    /// <summary>
    /// "Send", "Delivery", "Bounce" and the rest. What a configuration set event destination calls
    /// the field.
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// The same thing under the name the identity-level notifications use. Read as a fallback so
    /// that a topic subscribed the other way round is understood rather than silently ignored.
    /// </summary>
    public string NotificationType { get; init; } = string.Empty;

    public SesEventMail Mail { get; init; } = new();

    public SesEventBounce? Bounce { get; init; }

    public SesEventComplaint? Complaint { get; init; }

    public SesEventDelivery? Delivery { get; init; }

    public SesEventReject? Reject { get; init; }

    public SesEventFailure? Failure { get; init; }

    public SesEventDeliveryDelay? DeliveryDelay { get; init; }
}

/// <summary>The message the event is about. Present on every event type.</summary>
internal record SesEventMail
{
    /// <summary>The id SES assigned when it accepted the message. The key every row is found by.</summary>
    public string MessageId { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }

    public ImmutableList<string> Destination { get; init; } = [];

    /// <summary>
    /// The message tags the send carried, echoed back. This is how an event finds its participant:
    /// SES holds nothing of ours, so anything we want back has to have been put on the way out.
    /// </summary>
    /// <remarks>
    /// A list of values per name because tags are multi-valued in the API. Every tag this
    /// application sets has exactly one, and SES adds its own — <c>ses:configuration-set</c> among
    /// them — which is why the names are read by key rather than by position.
    /// </remarks>
    public Dictionary<string, List<string>> Tags { get; init; } = [];
}

internal record SesEventDelivery
{
    public DateTimeOffset Timestamp { get; init; }
}

internal record SesEventBounce
{
    /// <summary>"Permanent", "Transient" or "Undetermined".</summary>
    public string BounceType { get; init; } = string.Empty;

    /// <summary>"General", "NoEmail", "Suppressed" and the rest. The useful half of the pair.</summary>
    public string BounceSubType { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }

    public ImmutableList<SesEventBouncedRecipient> BouncedRecipients { get; init; } = [];
}

internal record SesEventBouncedRecipient
{
    public string EmailAddress { get; init; } = string.Empty;

    /// <summary>
    /// What the receiving server actually said, when it said anything. Written by a stranger's mail
    /// server, so it is truncated and encoded like any other text this application did not author.
    /// </summary>
    public string DiagnosticCode { get; init; } = string.Empty;
}

internal record SesEventComplaint
{
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>"abuse", "fraud", "not-spam" and the rest. Often absent — most feedback loops omit it.</summary>
    public string ComplaintFeedbackType { get; init; } = string.Empty;

    /// <summary>Who marked it as spam. The address that is blocked from being added again.</summary>
    public ImmutableList<SesEventComplainedRecipient> ComplainedRecipients { get; init; } = [];
}

internal record SesEventComplainedRecipient
{
    public string EmailAddress { get; init; } = string.Empty;
}

internal record SesEventReject
{
    /// <summary>Why SES would not send it. In practice always the virus scan.</summary>
    public string Reason { get; init; } = string.Empty;
}

internal record SesEventFailure
{
    public string ErrorMessage { get; init; } = string.Empty;
}

internal record SesEventDeliveryDelay
{
    /// <summary>"MailboxFull", "TransientCommunicationFailure" and the rest.</summary>
    public string DelayType { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }
}
