namespace GiftExchange.Library.Messaging;

/// <summary>
/// What one round of asking for gift ideas did, for whichever page has to report it.
/// </summary>
internal record AskRoundResult
{
    public required AskRoundOutcome Outcome { get; init; }

    /// <summary>
    /// Why the question was refused. <see cref="AskQuestionOutcome.Accepted"/> unless
    /// <see cref="Outcome"/> is <see cref="AskRoundOutcome.QuestionRefused"/>.
    /// </summary>
    public required AskQuestionOutcome QuestionOutcome { get; init; }

    /// <summary>
    /// The people chosen who survived checking against the database, so a form handed back keeps
    /// them ticked and nobody else.
    /// </summary>
    public required ImmutableList<Guid> Chosen { get; init; }

    /// <summary>Each person chosen, in the order offered, and whether they were asked.</summary>
    public required ImmutableList<AskAttempt> Attempts { get; init; }

    /// <summary>
    /// Whether the pick had written ideas down to be held until somebody asked, and they have just
    /// been sent to the asker.
    /// </summary>
    public required bool ReleasedHeldIdeas { get; init; }
}
