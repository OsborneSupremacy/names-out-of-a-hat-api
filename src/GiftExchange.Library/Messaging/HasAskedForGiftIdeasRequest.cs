namespace GiftExchange.Library.Messaging;

/// <summary>
/// The pair the share page asks about before passing on a held submission: has this giver asked for
/// gift ideas about this participant.
/// </summary>
/// <remarks>
/// Both ids rather than two positional Guids. They are the same type and the answer is not symmetric
/// — swapping them asks whether the writer has been asking about the person who drew them, which is
/// a different question with a different answer.
/// </remarks>
internal record HasAskedForGiftIdeasRequest
{
    /// <summary>Whoever drew <see cref="SubjectParticipantId"/>, and the only person held ideas go to.</summary>
    public required Guid AskerParticipantId { get; init; }

    public required Guid SubjectParticipantId { get; init; }
}
