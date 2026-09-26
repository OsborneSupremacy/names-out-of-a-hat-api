namespace GiftExchange.Library.Messaging;

internal record GiftExchangeEmailRequest
{
    public required Guid HatId { get; init; }

    public required string OrganizerEmail { get; init; }

    public required string RecipientEmail { get; init; }

    /// <summary>
    /// Which participant this is going to, so that the send can be tagged with it and the SES
    /// events that follow can be matched back to a row.
    /// </summary>
    /// <remarks>
    /// The address will not do that job. In test mode every message is diverted to one inbox, and
    /// even in live use a single address can be in several exchanges at once — so what comes back
    /// on an event has to name the participant, not the person.
    /// </remarks>
    public required Guid ParticipantId { get; init; }

    /// <summary>One of <see cref="EmailMessageType"/>. Tagged onto the send for the same reason.</summary>
    public required string MessageType { get; init; }

    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    /// <summary>
    /// Whose name goes on the From line, as "Jane Smith via Names Out Of A Hat". Empty for a message
    /// that is from this application rather than from anybody in particular.
    /// </summary>
    /// <remarks>
    /// Not required, and that is for the queue rather than for the callers. A message queued by the
    /// previous deployment has no such field, and it must still be sent — with the product's name
    /// alone — rather than failing to deserialize and landing in the dead letter queue.
    /// </remarks>
    public string SenderName { get; init; } = string.Empty;

    /// <summary>
    /// Where the <c>List-Unsubscribe</c> header points, or empty for no header.
    /// </summary>
    /// <remarks>
    /// The recipient's leave link, so only an invitation carries one, and never the organizer's own
    /// copy — there is no leaving an exchange you are running. Not required, for the same reason as
    /// <see cref="SenderName"/>.
    /// </remarks>
    public string UnsubscribeUrl { get; init; } = string.Empty;
}
