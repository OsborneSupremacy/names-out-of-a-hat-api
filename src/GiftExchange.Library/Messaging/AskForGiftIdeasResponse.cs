namespace GiftExchange.Library.Messaging;

/// <summary>
/// What a round of asking did: who was asked, who was held back by the weekly limit, and whether
/// the pick's held ideas came back at once.
/// </summary>
public record AskForGiftIdeasResponse
{
    /// <summary>Each person chosen, in the order offered, and whether they were asked.</summary>
    public required ImmutableList<AskAttempt> Attempts { get; init; }

    /// <summary>
    /// Whether the pick had written ideas to be passed on if anybody asked, and they have just been
    /// passed to the caller.
    /// </summary>
    public required bool ReleasedHeldIdeas { get; init; }
}
