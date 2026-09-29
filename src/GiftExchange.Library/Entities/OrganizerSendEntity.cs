namespace GiftExchange.Library.Entities;

/// <summary>
/// One address an organizer's exchange sent mail to, remembered against that organizer.
///
/// Outlives the exchange, as <see cref="OrganizerComplaintEntity"/> does, because everything it is
/// for is a question about the organizer rather than the exchange: how many people they have
/// mailed lately, which organizer a complaint about a deleted exchange belongs to, and what share
/// of their mail bounced.
/// </summary>
public class OrganizerSendEntity
{
    public required Guid OrganizerSendId { get; set; }

    /// <summary>
    /// The participant the message was addressed to — what the SES events that follow will name.
    /// </summary>
    /// <remarks>
    /// Not unique. Correcting an address resends to the same participant at a different address,
    /// and both are people this organizer has mailed.
    /// </remarks>
    public required Guid ParticipantId { get; set; }

    /// <summary>
    /// The organizer whose exchange sent the message, as a mailbox key (see
    /// <c>ToMailboxKey</c>), so that one inbox answers for every spelling of it.
    /// </summary>
    /// <remarks>
    /// An address rather than a <see cref="PersonEntity.PersonId"/>, for the reason given on
    /// <see cref="OrganizerComplaintEntity.OrganizerEmailNormalized"/>.
    /// </remarks>
    public required string OrganizerEmailNormalized { get; set; }

    /// <summary>Who the message went to, lower-cased and trimmed.</summary>
    public required string EmailNormalized { get; set; }

    /// <summary>When it was queued. What the send limit and the standing window measure against.</summary>
    public required DateTimeOffset SentAt { get; set; }
}
