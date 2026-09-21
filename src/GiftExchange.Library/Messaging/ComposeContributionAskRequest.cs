namespace GiftExchange.Library.Messaging;

/// <summary>
/// Everything the email asking somebody for ideas about another participant needs.
/// </summary>
internal record ComposeContributionAskRequest
{
    /// <summary>The exchange's name, as the organizer wrote it.</summary>
    public required string HatName { get; init; }

    /// <summary>Who the ideas are wanted about.</summary>
    public required string SubjectName { get; init; }

    /// <summary>The token behind the email's share button.</summary>
    public required string AskToken { get; init; }

    /// <summary>
    /// What the asker wanted to know, or empty when they left the box blank. Already through
    /// <see cref="Services.AskQuestionPolicy"/> and moderation, and still encoded before it is placed.
    /// </summary>
    public required string Question { get; init; }
}
