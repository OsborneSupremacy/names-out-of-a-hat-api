namespace GiftExchange.Library.Messaging;

/// <summary>
/// What a round of asking did, ready to be reported to the person who asked.
/// </summary>
/// <remarks>
/// A record rather than a third positional argument, for the reason
/// <see cref="ComposeShareIdeasFormRequest"/> gives: the page already takes a name and a list, and a
/// bare bool on the end says nothing at the call site about which outcome it describes.
/// </remarks>
internal record ComposeAskResultsRequest
{
    /// <summary>Who the ideas were wanted about: the asker's own pick.</summary>
    public required string SubjectName { get; init; }

    /// <summary>Each person chosen, and whether they were asked.</summary>
    public required ImmutableList<AskAttempt> Attempts { get; init; }

    /// <summary>
    /// Whether the subject had already written ideas down to be held until somebody asked, and they
    /// have just been sent to the asker.
    /// </summary>
    /// <remarks>
    /// Worth saying on this page: the email arrives on its own otherwise, looking like an answer that
    /// came back impossibly fast.
    /// </remarks>
    public required bool ReleasedHeldIdeas { get; init; }
}
