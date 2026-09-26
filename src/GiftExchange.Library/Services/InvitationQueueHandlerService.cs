using Amazon.Lambda.SQSEvents;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;

namespace GiftExchange.Library.Services;

internal class InvitationQueueHandlerService
{
    private readonly IAmazonSimpleEmailService _sesClient;

    private readonly JsonService _jsonService;

    private const string TestRecipient = "osborne.ben@gmail.com";

    private readonly bool _liveMode;

    /// <summary>
    /// The SES configuration set that publishes what happens to this message.
    /// </summary>
    /// <remarks>
    /// Naming it on the send is the entire subscription: without it SES sends the mail and reports
    /// nothing, which is how this application ran until delivery tracking existed — a bounced
    /// invitation and a delivered one looked exactly alike.
    ///
    /// Read once, at construction, and empty is allowed. An environment that has not been given the
    /// name still sends mail; it just goes back to reporting nothing, rather than failing every
    /// invitation over a missing variable.
    /// </remarks>
    private readonly string _configurationSet;

    public InvitationQueueHandlerService(
        IAmazonSimpleEmailService sesClient,
        JsonService jsonService
        )
    {
        _sesClient = sesClient;
        _jsonService = jsonService;
        _liveMode = EnvReader.TryGetBooleanValue("LIVE_MODE", out var boolOut) && boolOut;
        _configurationSet = EnvReader.TryGetStringValue("SES_CONFIGURATION_SET", out var configurationSet)
            ? configurationSet ?? string.Empty
            : string.Empty;
    }

    public async Task ProcessRecordAsync(SQSEvent.SQSMessage record, ILambdaContext context)
    {
        var invitation = _jsonService.DeserializeDefault<GiftExchangeEmailRequest>(record.Body);

        if (invitation is null)
            throw new AggregateException($"Invalid message body: {record.Body}");

        context.Logger.LogInformation(
            $"Sending email to {invitation.RecipientEmail} with subject '{invitation.Subject}'");

        var recipient = _liveMode ? invitation.RecipientEmail : TestRecipient;

        var message = OutgoingEmail.Compose(
            OutgoingEmail.Sender(invitation.SenderName),
            recipient,
            invitation.Subject + (_liveMode ? string.Empty : " - TEST MODE"),
            invitation.HtmlBody);

        // The leave link, offered to the mail client as the way to stop hearing from us. A recipient
        // who wants out and finds no unsubscribe option in their client reaches for "report spam"
        // instead, and a complaint costs the whole SES account rather than one exchange.
        //
        // No List-Unsubscribe-Post, and that is deliberate. RFC 8058's one-click unsubscribe has the
        // mail provider POST to this address with no human on the page, and a POST here is the
        // leave itself: it would take the participant out of the exchange, send the organizer back
        // to the hat and tell everybody else to disregard their name, all from a button in a mail
        // client's toolbar. Without the Post header a client can only open the address, which lands
        // on the confirmation page like any other click on the link.
        if (!string.IsNullOrWhiteSpace(invitation.UnsubscribeUrl))
            message.Headers.Add("List-Unsubscribe", $"<{invitation.UnsubscribeUrl}>");

        using var buffer = await OutgoingEmail.ToRawAsync(message).ConfigureAwait(false);

        // Raw rather than SendEmail, which can set neither a header nor a display name that isn't
        // plain ASCII.
        var sendRequest = new SendRawEmailRequest
        {
            RawMessage = new RawMessage { Data = buffer },
            // Tagged even in test mode. The events are about a real message that really was sent,
            // and recording them against the participant it was meant for is what makes the whole
            // path testable without live addresses.
            Tags =
            [
                new MessageTag { Name = SesMessageTags.ParticipantId, Value = invitation.ParticipantId.ToString() },
                new MessageTag { Name = SesMessageTags.MessageType, Value = invitation.MessageType }
            ]
        };

        // Set rather than initialized, because the property may not be set at all: SES rejects an
        // empty configuration set name outright, so an unconfigured environment has to send the
        // request without the field rather than with a blank one.
        if (!string.IsNullOrWhiteSpace(_configurationSet))
            sendRequest.ConfigurationSetName = _configurationSet;

        var response = await _sesClient
            .SendRawEmailAsync(sendRequest)
            .ConfigureAwait(false);

        context.Logger.LogInformation($"Email sent to {invitation.RecipientEmail}. MessageId: {response.MessageId}");
    }
}
