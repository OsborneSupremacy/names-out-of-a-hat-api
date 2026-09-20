namespace GiftExchange.Library.Entities;

/// <summary>
/// One participant having asked for gift ideas about the person whose name they drew.
///
/// It exists for submissions held back by <see cref="GiftIdeaEntity.HoldUntilAsked"/>, which reach
/// the one person who drew the writer and only once that person has asked. Nothing else recorded the
/// asking in a way that lasts: asking somebody else about a pick writes a
/// <see cref="GiftIdeaAskEntity"/>, but asking the pick themselves wrote no row at all — a token, an
/// email, and a throttle entry in DynamoDB keyed on a different pair and gone within a week.
///
/// And the order cannot be relied on. Somebody may ask before the person whose name they hold has
/// written anything, so held ideas written afterwards need something still standing to consult.
/// </summary>
/// <remarks>
/// Not a synonym of <see cref="GiftIdeaAskEntity"/>, despite the resemblance. An ask is a message
/// sent to a third party, with a token of its own and replies filed against it; this is a standing
/// fact about a giver and their pick, which no message can carry.
/// </remarks>
public class GiftIdeaEnquiryEntity
{
    public required Guid GiftIdeaEnquiryId { get; set; }

    /// <summary>
    /// Who asked, and so the single person anything released against this row is sent to. Always
    /// whoever drew <see cref="SubjectParticipantId"/>: asking is only ever offered about your own
    /// pick.
    /// </summary>
    /// <remarks>
    /// No navigation property, for the reason given on <see cref="GiftIdeaEntity.ParticipantId"/>.
    /// The same applies to the id below.
    /// </remarks>
    public required Guid AskerParticipantId { get; set; }

    /// <summary>
    /// Who they asked about, recorded rather than followed back through
    /// <see cref="ParticipantEntity.PickedRecipientParticipantId"/> later, for the reason
    /// <see cref="GiftIdeaAskEntity.SubjectParticipantId"/> gives. A row naming a giver who no
    /// longer holds that name matches nobody, so the ideas stay held — which is the right answer.
    /// </summary>
    public required Guid SubjectParticipantId { get; set; }

    /// <summary>
    /// When they first asked. One row per pair, so this is the first time rather than the latest:
    /// asking again changes nothing about what is owed to them.
    /// </summary>
    public required DateTimeOffset RequestedAt { get; set; }

    /// <summary>
    /// When a held submission was last passed on against this enquiry, or
    /// <see cref="DateTimeOffset.MinValue"/> if none ever has been.
    /// </summary>
    /// <remarks>
    /// A release is sent when the subject's newest submission is held and was written after this
    /// stamp. That is what makes it happen once per submission rather than once per ask, and what
    /// lets a send that was dropped be picked up by the next one — the sender here cannot report a
    /// failure.
    /// </remarks>
    public required DateTimeOffset ReleasedAt { get; set; }
}
