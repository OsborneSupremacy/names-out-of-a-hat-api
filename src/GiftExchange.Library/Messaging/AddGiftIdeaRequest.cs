namespace GiftExchange.Library.Messaging;

/// <summary>
/// One submission a participant made about themselves, on its way to being stored.
/// </summary>
/// <remarks>
/// A record rather than three positional arguments, and the flag is why: a bool at the end of a
/// parameter list says nothing at the call site about which way round it means, and this one decides
/// whether anybody ever reads what was written.
/// </remarks>
internal record AddGiftIdeaRequest
{
    /// <summary>Who wrote it. A participant, not a person: this is said within one exchange.</summary>
    public required Guid ParticipantId { get; init; }

    /// <summary>Their own words, checked by the content policy and moderation before reaching here.</summary>
    public required string Ideas { get; init; }

    /// <summary>
    /// Whether they asked for it to be held back until the person who drew them asks for gift ideas.
    /// </summary>
    public required bool HoldUntilAsked { get; init; }
}
