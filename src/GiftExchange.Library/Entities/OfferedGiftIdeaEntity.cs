namespace GiftExchange.Library.Entities;

/// <summary>
/// Ideas one participant offered about another without being asked.
///
/// The third kind of gift idea, and the one distinction worth holding on to is who the text is for.
/// <see cref="GiftIdeaEntity"/> is what somebody said about themselves, and it goes to whoever drew
/// them. <see cref="ContributedGiftIdeaEntity"/> is what a third party wrote because somebody asked,
/// and <see cref="GiftIdeaAskEntity"/> records who asked — so its recipient is fixed the moment the
/// ask goes out. This is what a third party wrote unprompted, and its recipient is whoever holds the
/// subject's name when it is sent.
///
/// Which is why there is no column here for who received it. There was never a row to freeze it on,
/// and freezing it would be wrong anyway: nothing has been promised to anybody in advance.
///
/// Rows accumulate rather than being edited, as they do in the other two, for the reasons given
/// there.
/// </summary>
public class OfferedGiftIdeaEntity
{
    public required Guid OfferedGiftIdeaId { get; set; }

    /// <summary>
    /// Who wrote it, and who the reader is told wrote it.
    /// </summary>
    /// <remarks>
    /// Attribution is the point rather than a side effect. A suggestion the reader cannot attribute
    /// is one they have no way to weigh, and the page says so before anything is typed.
    ///
    /// No navigation property, for the reason given on <see cref="GiftIdeaEntity.ParticipantId"/>.
    /// The same applies to the id below.
    /// </remarks>
    public required Guid AuthorParticipantId { get; set; }

    /// <summary>Who the ideas are about. Never told that any of this happened.</summary>
    public required Guid SubjectParticipantId { get; set; }

    /// <summary>Their own words, never the application's, and never empty.</summary>
    public required string Ideas { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
