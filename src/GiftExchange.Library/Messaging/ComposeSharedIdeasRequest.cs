namespace GiftExchange.Library.Messaging;

/// <summary>
/// What the page shown after a successful submission needs.
/// </summary>
/// <remarks>
/// A record rather than a route, a string and a bool, for the reason
/// <see cref="ComposeShareIdeasFormRequest"/> gives about parameter lists that grow.
/// </remarks>
internal record ComposeSharedIdeasRequest
{
    /// <summary>Who wrote it, and whether the ideas are their own or about somebody else.</summary>
    public required GiftIdeaRoute Route { get; init; }

    /// <summary>Exactly what was submitted, echoed back. Written by a participant, so it is encoded.</summary>
    public required string Ideas { get; init; }

    /// <summary>
    /// Whether they asked for it to be held until somebody asks.
    /// </summary>
    /// <remarks>
    /// Says which of the two descriptions the page gives, and nothing else. In particular it does
    /// not say whether the ideas have just gone out — a held submission whose reader has already
    /// asked is sent immediately, and the page says the same words either way, because saying
    /// otherwise would tell the writer their giver had been asking about them.
    /// </remarks>
    public required bool HoldUntilAsked { get; init; }
}
