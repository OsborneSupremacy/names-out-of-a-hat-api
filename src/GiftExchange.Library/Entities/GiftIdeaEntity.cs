namespace GiftExchange.Library.Entities;

/// <summary>
/// One thing a participant sent in about what they would like, shared with the single person who
/// drew them and with nobody else — the organizer included.
///
/// Rows accumulate rather than being edited. The newest one for a participant is what gets
/// forwarded, so a second submission reads as a replacement without the first ceasing to exist:
/// text pulled out of an email is a guess at where the quoted reply began, and a guess that goes
/// wrong is only recoverable while what preceded it is still here.
/// </summary>
public class GiftIdeaEntity
{
    public required Guid GiftIdeaId { get; set; }

    /// <summary>Who wrote it. A participant, not a person: this is said within one exchange.</summary>
    /// <remarks>
    /// Deliberately has no navigation property, for the reason
    /// <see cref="ParticipantEntity.PickedRecipientParticipantId"/> and
    /// <see cref="HatEntity.CopiedFromHatId"/> have none. EF turns a reference navigation into a
    /// real foreign key wherever the provider supports one, and the databases this suite builds
    /// would then refuse to delete a participant who had written something while DSQL, which the
    /// application treats as having no foreign keys, would allow it. Cleanup lives in the provider
    /// alongside the rest of it.
    /// </remarks>
    public required Guid ParticipantId { get; set; }

    /// <summary>Their own words, never the application's, and never empty — an empty submission is refused on the way in.</summary>
    public required string Ideas { get; set; }

    /// <summary>
    /// When it was sent. This is what decides which submission is newest, rather than the id, so
    /// that the winner turns on a value the application states outright.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Whether this was written to be held back until the person who drew the writer asks for gift
    /// ideas, rather than sent to them the moment it arrived.
    /// </summary>
    /// <remarks>
    /// Set when the row is written and never changed afterwards, which suits a table nothing edits
    /// in place. Clearing it on release would untick the box the next time the writer opened the
    /// form, and so tell them their giver had been asking about them — the one thing the Ask exists
    /// to avoid. Whether anything has been passed on lives on <see cref="GiftIdeaEnquiryEntity"/>
    /// instead.
    /// </remarks>
    public required bool HoldUntilAsked { get; set; }

    /// <summary>
    /// The SES message id this arrived in, from when gift ideas were shared by email. Always the
    /// empty string now that they are shared from a page. Kept because DSQL cannot drop the column.
    /// </summary>
    public required string InboundMessageId { get; set; }
}
