namespace GiftExchange.Library.Entities;

/// <summary>
/// An address that hard-bounced mail from an organizer's exchange, remembered against that
/// organizer.
///
/// The bounce counterpart of <see cref="OrganizerComplaintEntity"/>, and durable for the same
/// reason: <see cref="ParticipantEmailDeliveryEntity"/> records the bounce too, but its rows go
/// with the exchange, and an organizer mailing a list of addresses that do not exist would delete
/// their way out of the record.
/// </summary>
public class OrganizerBounceEntity
{
    public required Guid OrganizerBounceId { get; set; }

    /// <summary>
    /// The organizer whose exchange sent the message, as a mailbox key (see
    /// <c>ToMailboxKey</c>), so that one inbox answers for every spelling of it.
    /// </summary>
    /// <remarks>
    /// An address rather than a <see cref="PersonEntity.PersonId"/>, for the reason given on
    /// <see cref="OrganizerComplaintEntity.OrganizerEmailNormalized"/>.
    /// </remarks>
    public required string OrganizerEmailNormalized { get; set; }

    /// <summary>The address that bounced, lower-cased and trimmed.</summary>
    public required string EmailNormalized { get; set; }

    /// <summary>When SES says the bounce happened. What the standing window is measured against.</summary>
    public required DateTimeOffset BouncedAt { get; set; }
}
