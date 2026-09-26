using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using MimeKit;

namespace GiftExchange.Library.Services;

/// <summary>
/// Sends the messages this application generates in response to something rather than on somebody's
/// instruction: confirmations, refusals, forwards and Asks.
/// </summary>
/// <remarks>
/// One place, because it was three, and the duplication had already cost something. Every copy read
/// LIVE_MODE for itself, and the function hosting one of them never had it set, so magic link
/// emails went out marked TEST MODE while invitations went out correctly — no error, no log line,
/// just a flag reading false where nobody had set it.
///
/// Built as raw MIME rather than through SendEmail, which has no way to set a header. The header in
/// question is <c>Auto-Submitted: auto-replied</c>, from RFC 3834, and it is what stops a
/// well-behaved autoresponder at the far end from answering this and being answered in turn. Every
/// message sent here is a response to something, so every one of them needs it.
///
/// Nothing sent here carries a Reply-To. On a forward that is load-bearing rather than tidy: a
/// reply that reached the sender would tell them who holds their name. For the same reason the From
/// line names only the product, never a person: a forward or an Ask that came "via" somebody would
/// say who.
/// </remarks>
[UsedImplicitly]
internal class AutomaticEmailSender
{
    private const string TestRecipient = "osborne.ben@gmail.com";

    private readonly IAmazonSimpleEmailService _sesClient;

    private readonly ILogger<AutomaticEmailSender> _logger;

    private readonly bool _liveMode;

    public AutomaticEmailSender(IAmazonSimpleEmailService sesClient, ILogger<AutomaticEmailSender> logger)
    {
        _sesClient = sesClient ?? throw new ArgumentNullException(nameof(sesClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _liveMode = EnvReader.TryGetBooleanValue("LIVE_MODE", out var boolOut) && boolOut;
    }

    /// <summary>
    /// Sends one message, and never throws.
    /// </summary>
    /// <remarks>
    /// Failures are logged and swallowed on purpose. Everything calling this has already done the
    /// durable part of its work — stored a submission, claimed a throttle slot — and throwing here
    /// would undo none of it while causing the caller's whole event to be retried, which for a
    /// queue handler means doing that durable part twice.
    ///
    /// That promise used to hold only from the SES call onwards, and parsing the recipient sat
    /// above it. An empty envelope sender — which is what a bounce carries, by design — reached
    /// here as an empty recipient and threw out of the whole handler.
    /// </remarks>
    public async Task SendAsync(string recipient, string subject, string htmlBody)
    {
        // Checked against the address we were asked to write to rather than the one this send will
        // actually use, so that an unusable address is refused the same way in both modes. Falling
        // through to the test recipient would send a real message on behalf of a caller who had
        // nobody to write to.
        if (string.IsNullOrWhiteSpace(recipient) || !MailboxAddress.TryParse(recipient, out _))
        {
            _logger.LogWarning("Declined to send an automatic email: the recipient is not an address.");
            return;
        }

        var destination = _liveMode ? recipient : TestRecipient;

        var message = OutgoingEmail.Compose(
            OutgoingEmail.Sender(string.Empty),
            destination,
            subject + (_liveMode ? string.Empty : " - TEST MODE"),
            htmlBody);

        message.Headers.Add("Auto-Submitted", "auto-replied");

        using var buffer = await OutgoingEmail.ToRawAsync(message).ConfigureAwait(false);

        try
        {
            await _sesClient
                .SendRawEmailAsync(new SendRawEmailRequest { RawMessage = new RawMessage { Data = buffer } })
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to send an automatic email.");
        }
    }
}
