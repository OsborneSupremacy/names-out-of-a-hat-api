using MimeKit;

namespace GiftExchange.Library.Services;

/// <summary>
/// The parts of an outgoing message that every sender here must get the same: who it is from, and
/// that it carries a plain-text part as well as HTML.
/// </summary>
/// <remarks>
/// Most of the people these messages reach have never heard of this application and did not sign
/// up for anything. A friend or relative added them, and the first they know of it is an email from
/// a domain they don't recognise. What decides whether that email reaches them, and whether they
/// press "report spam" when it does, is mostly settled here: a From line with no name on it and a
/// body that is HTML alone both count against a sender, and a complaint counts against the whole
/// SES account, magic links included.
/// </remarks>
internal static class OutgoingEmail
{
    internal const string SenderAddress = "donotreply@mail.namesoutofahat.com";

    internal const string ProductName = "Names Out Of A Hat";

    /// <summary>
    /// The From line, naming the person the message is really from when there is one.
    /// </summary>
    /// <remarks>
    /// "Jane Smith via Names Out Of A Hat" rather than the bare address, because a recipient
    /// recognises the name of somebody they know long before they recognise a domain. The product
    /// name stays in it, so a display name cannot be made to read as though the message came from
    /// somebody's bank: whatever an organizer calls themselves, the line still says where it was
    /// sent through, and the address beside it is still ours.
    ///
    /// Whitespace is collapsed first. A name is typed into a form and could carry a line break, and
    /// although MimeKit encodes a display name rather than writing it raw, a From line is no place
    /// to find out how every mail client renders one.
    /// </remarks>
    /// <param name="onBehalfOf">
    /// The organizer's name, or empty for a message that is from this application itself — a
    /// sign-in link, or a notice sent to the organizer about their own exchange.
    /// </param>
    internal static MailboxAddress Sender(string onBehalfOf)
    {
        var name = string.Join(' ', (onBehalfOf ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var displayName = string.IsNullOrEmpty(name) ? ProductName : $"{name} via {ProductName}";

        return new MailboxAddress(displayName, SenderAddress);
    }

    /// <summary>
    /// A message with both parts, from the given sender, to one recipient.
    /// </summary>
    internal static MimeMessage Compose(MailboxAddress from, string to, string subject, string htmlBody)
    {
        var message = new MimeMessage
        {
            Subject = subject,
            Body = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = EmailPlainText.FromHtml(htmlBody)
            }.ToMessageBody()
        };

        message.From.Add(from);
        message.To.Add(MailboxAddress.Parse(to));

        return message;
    }

    /// <summary>The message as the bytes SES's raw send takes.</summary>
    internal static async Task<MemoryStream> ToRawAsync(MimeMessage message)
    {
        var buffer = new MemoryStream();
        await message.WriteToAsync(buffer).ConfigureAwait(false);
        buffer.Position = 0;
        return buffer;
    }
}
