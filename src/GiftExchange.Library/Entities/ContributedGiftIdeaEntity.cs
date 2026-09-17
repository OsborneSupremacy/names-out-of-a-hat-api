namespace GiftExchange.Library.Entities;

/// <summary>
/// Something one participant suggested about another, in reply to being asked.
///
/// Deliberately not a <see cref="GiftIdeaEntity"/>. That is what somebody said about themselves,
/// and it is forwarded to whoever drew them; this is a third party's guess, and it goes only to the
/// person who asked for it. Storing them in one table would make it possible for a suggestion Dad
/// made about Mom to reach Mom's giver as though Mom had written it.
///
/// Rows accumulate rather than being edited, as they do in <see cref="GiftIdeaEntity"/>, for the
/// reasons given there.
/// </summary>
public class ContributedGiftIdeaEntity
{
    public required Guid ContributedGiftIdeaId { get; set; }

    /// <summary>
    /// The ask this answers, which is where everything else about it lives: who wrote it, who it is
    /// about, and who it was sent to. Not repeated here, where a second copy could disagree.
    /// </summary>
    /// <remarks>No navigation property, for the reason given on <see cref="GiftIdeaEntity.ParticipantId"/>.</remarks>
    public required Guid GiftIdeaAskId { get; set; }

    /// <summary>Their own words, never the application's, and never empty.</summary>
    public required string Ideas { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The SES message id this arrived in, from when gift ideas were shared by email. Always the
    /// empty string now, for the reason <see cref="GiftIdeaEntity.InboundMessageId"/> gives.
    /// </summary>
    public required string InboundMessageId { get; set; }
}
