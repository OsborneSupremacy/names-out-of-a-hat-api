using Amazon.Lambda.SQSEvents;
using GiftExchange.Library.Entities.Configurations;

namespace GiftExchange.Library.Services;

/// <summary>
/// Turns one SES event notification into a row saying what happened to a participant's email.
/// </summary>
/// <remarks>
/// This is the only writer of <c>participant_email_delivery</c>, and that is deliberate. SES
/// publishes a Send event of its own the moment it accepts a message, so the whole lifecycle
/// arrives on this one channel — which means the sending path does not have to write anything, does
/// not need database access, and cannot race the events that follow its own send.
///
/// Everything that reaches here is about a message that has already gone. Nothing this service does
/// can affect it, so nothing here fails a send; the worst outcome is a status the organizer does
/// not see.
///
/// The one thing it does beyond recording is act on a complaint: whoever marked a message as spam is
/// put on the do-not-add-anywhere list, so the next organizer is stopped before sending rather than
/// after.
/// </remarks>
[UsedImplicitly]
internal class DeliveryEventsService
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly JsonService _jsonService;

    private readonly ILogger<DeliveryEventsService> _logger;

    public DeliveryEventsService(
        GiftExchangeProvider giftExchangeProvider,
        JsonService jsonService,
        ILogger<DeliveryEventsService> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _jsonService = jsonService ?? throw new ArgumentNullException(nameof(jsonService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles one queued event.
    /// </summary>
    /// <remarks>
    /// The subscription uses raw message delivery, so the body is the SES event itself rather than
    /// an SNS envelope wrapping it as a string.
    /// </remarks>
    /// <returns>Whether a row was written. False is an ordinary outcome, not a failure.</returns>
    public async Task<bool> ProcessRecordAsync(SQSEvent.SQSMessage record)
    {
        var notification = _jsonService.DeserializeDefault<SesDeliveryEvent>(record.Body);

        if (notification is null)
        {
            // Thrown rather than logged. A body that is not JSON at all is a wiring mistake — the
            // wrong topic, or raw message delivery turned off — and failing puts it in the dead
            // letter queue where somebody will find it, instead of discarding it quietly.
            throw new AggregateException($"Invalid delivery event body: {record.Body}");
        }

        return await ProcessAsync(notification).ConfigureAwait(false);
    }

    internal async Task<bool> ProcessAsync(SesDeliveryEvent notification)
    {
        var eventType = string.IsNullOrWhiteSpace(notification.EventType)
            ? notification.NotificationType
            : notification.EventType;

        var status = StatusOf(eventType);

        if (status == DeliveryStatus.Unknown)
        {
            // Open, Click, Subscription, and anything SES adds later. None are published by the
            // configuration set this application creates, so reaching here means somebody enabled
            // one; saying so is more useful than a silent drop.
            _logger.LogInformation(
                "Ignoring a {EventType} event for message {MessageId}: nothing here records it.",
                eventType,
                notification.Mail.MessageId);
            return false;
        }

        if (status == DeliveryStatus.Complained)
            await BlockComplainantsAsync(notification).ConfigureAwait(false);

        var participantId = ParticipantIdOf(notification);

        if (participantId == Guid.Empty)
        {
            // The configuration set is attached to sends, not to the identity, so in practice this
            // only happens if a send is added that forgets the tag. Logged rather than thrown: the
            // message really was sent, and there is nothing to retry.
            _logger.LogWarning(
                "A {EventType} event for message {MessageId} carried no usable {TagName} tag.",
                eventType,
                notification.Mail.MessageId,
                SesMessageTags.ParticipantId);
            return false;
        }

        var written = await _giftExchangeProvider
            .RecordDeliveryEventAsync(new ParticipantEmailDelivery
            {
                ParticipantId = participantId,
                MessageType = MessageTypeOf(notification),
                SesMessageId = notification.Mail.MessageId,
                Status = status,
                Detail = Truncate(DetailOf(notification, status)),
                OccurredAt = OccurredAtOf(notification, status)
            })
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Message {MessageId} to participant {ParticipantId} is {Status}. Row {Written}.",
            notification.Mail.MessageId,
            participantId,
            status,
            written ? "written" : "left as it was");

        return written;
    }

    /// <summary>
    /// Puts everybody who marked the message as spam on the do-not-add-anywhere list.
    /// </summary>
    /// <remarks>
    /// Permanent, like every other row on that list: somebody who wants back in is added by an
    /// organizer who asks them first. Nothing short of that is safe, because an address that has
    /// told its mailbox provider we are spam will keep saying so, and every further complaint counts
    /// against the account's sending reputation.
    ///
    /// Any feedback type but "not-spam" blocks. Most feedback loops send none at all, so waiting for
    /// "abuse" would let the majority of complaints through; "not-spam" is the one value that says
    /// the opposite of a complaint.
    ///
    /// Done before, and regardless of, the participant tag. The tag is how the event finds a row to
    /// show an organizer; the complaint is about the address, whichever send provoked it.
    ///
    /// A failure here throws, so the event is retried rather than recorded without its block. The
    /// provider tolerates the retry: a second write for an address already on the list is a no-op.
    /// </remarks>
    private async Task BlockComplainantsAsync(SesDeliveryEvent notification)
    {
        if (notification.Complaint is not { } complaint)
            return;

        if (string.Equals(complaint.ComplaintFeedbackType.TrimNullSafe(), "not-spam", StringComparison.OrdinalIgnoreCase))
            return;

        // Null rather than empty when the field is absent, whatever the initializer says, so the
        // guard is needed. An event naming nobody blocks nobody.
        var complainants = (complaint.ComplainedRecipients ?? [])
            .Select(recipient => recipient.EmailAddress)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var email in complainants)
        {
            await _giftExchangeProvider
                .RecordDoNotAddAsync(new RecordDoNotAddRequest
                {
                    Email = email,
                    // Not leaving any one exchange, and the provider writes no exchange refusal
                    // for the empty id.
                    HatId = Guid.Empty,
                    OrganizerEmail = string.Empty,
                    BlockOrganizer = false,
                    BlockAnywhere = true
                })
                .ConfigureAwait(false);

            _logger.LogWarning(
                "Message {MessageId} was marked as spam ({FeedbackType}); the recipient can no longer be added to any exchange.",
                notification.Mail.MessageId,
                string.IsNullOrWhiteSpace(complaint.ComplaintFeedbackType) ? "no feedback type" : complaint.ComplaintFeedbackType);
        }
    }

    /// <summary>
    /// Maps an SES event type onto the status this application keeps.
    /// </summary>
    /// <remarks>
    /// "Rendering Failure" is spelled two ways in AWS's own documentation depending on whether the
    /// subject is the event type or the destination's matching type, so both are accepted. The
    /// comparison is ordinal and case-insensitive because this string comes off the wire.
    /// </remarks>
    private static string StatusOf(string eventType) => eventType.TrimNullSafe() switch
    {
        var value when Is(value, "Send") => DeliveryStatus.Sent,
        var value when Is(value, "Delivery") => DeliveryStatus.Delivered,
        var value when Is(value, "DeliveryDelay") => DeliveryStatus.Delayed,
        var value when Is(value, "Bounce") => DeliveryStatus.Bounced,
        var value when Is(value, "Complaint") => DeliveryStatus.Complained,
        var value when Is(value, "Reject") => DeliveryStatus.Rejected,
        var value when Is(value, "Rendering Failure") || Is(value, "RenderingFailure") =>
            DeliveryStatus.Failed,
        _ => DeliveryStatus.Unknown
    };

    private static bool Is(string value, string eventType) =>
        string.Equals(value, eventType, StringComparison.OrdinalIgnoreCase);

    private static Guid ParticipantIdOf(SesDeliveryEvent notification) =>
        Guid.TryParse(SesMessageTags.Read(notification.Mail, SesMessageTags.ParticipantId), out var participantId)
            ? participantId
            : Guid.Empty;

    private static string MessageTypeOf(SesDeliveryEvent notification)
    {
        var tagged = SesMessageTags.Read(notification.Mail, SesMessageTags.MessageType);

        // Anything unrecognised is recorded as unspecified rather than stored as it arrived. The
        // column is twenty characters and this value is a stranger's until it matches something
        // known.
        return EmailMessageTypes.All.Contains(tagged, StringComparer.Ordinal)
            ? tagged
            : EmailMessageType.Unspecified;
    }

    /// <summary>
    /// When the event happened, taken from the event's own block where it has one.
    /// </summary>
    /// <remarks>
    /// Falling back to the mail timestamp is right rather than merely convenient: that is when the
    /// message was accepted, and Send and Reject both happen at that moment and carry no timestamp
    /// of their own. It is also what orders the rows, so it has to be filled for every event.
    /// </remarks>
    private static DateTimeOffset OccurredAtOf(SesDeliveryEvent notification, string status)
    {
        var timestamp = status switch
        {
            var value when value == DeliveryStatus.Delivered => notification.Delivery?.Timestamp,
            var value when value == DeliveryStatus.Bounced => notification.Bounce?.Timestamp,
            var value when value == DeliveryStatus.Complained => notification.Complaint?.Timestamp,
            var value when value == DeliveryStatus.Delayed => notification.DeliveryDelay?.Timestamp,
            _ => null
        };

        return timestamp is null || timestamp == default(DateTimeOffset)
            ? notification.Mail.Timestamp
            : timestamp.Value;
    }

    /// <summary>
    /// The part an organizer can act on: not that it failed, but what the far end said about why.
    /// </summary>
    private static string DetailOf(SesDeliveryEvent notification, string status)
    {
        if (status == DeliveryStatus.Bounced && notification.Bounce is { } bounce)
        {
            var recipient = bounce.BouncedRecipients.FirstOrDefault();

            // Permanent/General is the useful half — it separates "this address does not exist"
            // from "this mailbox is full today" — and the diagnostic is the receiving server's own
            // sentence, which is usually the only thing that names the actual problem.
            return Join([
                Join([bounce.BounceType, bounce.BounceSubType], "/"),
                recipient?.DiagnosticCode ?? string.Empty
            ], ": ");
        }

        if (status == DeliveryStatus.Complained)
            return notification.Complaint?.ComplaintFeedbackType ?? string.Empty;

        if (status == DeliveryStatus.Rejected)
            return notification.Reject?.Reason ?? string.Empty;

        if (status == DeliveryStatus.Failed)
            return notification.Failure?.ErrorMessage ?? string.Empty;

        if (status == DeliveryStatus.Delayed)
            return notification.DeliveryDelay?.DelayType ?? string.Empty;

        // Sent and Delivered. Nothing happened that needs explaining.
        return string.Empty;
    }

    private static string Join(IEnumerable<string> parts, string separator) =>
        string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>
    /// Cuts the detail down to what the column holds.
    /// </summary>
    /// <remarks>
    /// This text is written by whatever mail server rejected the message, so its length is nobody's
    /// to promise. Truncating here rather than letting the insert fail keeps a verbose stranger from
    /// costing us the status as well as the explanation.
    /// </remarks>
    private static string Truncate(string detail)
    {
        var trimmed = detail.TrimNullSafe();

        return trimmed.Length <= ParticipantEmailDeliveryEntityConfiguration.DetailMaxLength
            ? trimmed
            : trimmed[..ParticipantEmailDeliveryEntityConfiguration.DetailMaxLength];
    }
}
