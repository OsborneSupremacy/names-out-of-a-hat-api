namespace GiftExchange.Library.Messaging;

/// <summary>
/// One participant's unprompted suggestion about another, on its way to being stored.
/// </summary>
/// <remarks>
/// A record rather than three positional arguments, and the two participant ids are why: both are
/// <c>Guid</c>, both name somebody in the same exchange, and swapping them at a call site would
/// store a suggestion as though the subject had written it about its author. Named properties put
/// that mistake where it would be made.
/// </remarks>
internal record AddOfferedGiftIdeaRequest
{
    /// <summary>Who wrote it, and who the reader will be told wrote it.</summary>
    public required Guid AuthorParticipantId { get; init; }

    /// <summary>Who it is about. Never told that any of this happened.</summary>
    public required Guid SubjectParticipantId { get; init; }

    /// <summary>Their own words, checked by the content policy and moderation before reaching here.</summary>
    public required string Ideas { get; init; }
}
