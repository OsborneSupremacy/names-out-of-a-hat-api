namespace GiftExchange.Library.Entities;

/// <summary>
/// Somebody who marked mail from an organizer's exchange as spam, remembered against that
/// organizer.
///
/// The durable half of a complaint. <see cref="ParticipantEmailDeliveryEntity"/> records the same
/// event, but its rows go with the participant and the exchange; these do not, because an organizer
/// who could delete their way out of a complaint would, and the one who would is the one this is
/// for.
/// </summary>
public class OrganizerComplaintEntity
{
    public required Guid OrganizerComplaintId { get; set; }

    /// <summary>
    /// The organizer whose exchange sent the message, lower-cased and trimmed.
    /// </summary>
    /// <remarks>
    /// An address rather than a <see cref="PersonEntity.PersonId"/>, for the reason given on
    /// <see cref="DoNotAddByOrganizerEntity.OrganizerEmailNormalized"/> — and because the two tables
    /// are read together, by the same check, from the same address.
    /// </remarks>
    public required string OrganizerEmailNormalized { get; set; }

    /// <summary>Who complained, lower-cased and trimmed.</summary>
    public required string EmailNormalized { get; set; }

    /// <summary>When SES says the complaint was made. What the standing window is measured against.</summary>
    public required DateTimeOffset ComplainedAt { get; set; }
}
