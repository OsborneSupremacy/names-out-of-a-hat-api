namespace GiftExchange.Library.Messaging;

/// <summary>
/// Everything the email asking somebody what they would like needs.
/// </summary>
internal record ComposeAskRequest
{
    /// <summary>The exchange's name, as the organizer wrote it.</summary>
    public required string HatName { get; init; }

    /// <summary>The token behind the email's share button.</summary>
    public required string GiftIdeasToken { get; init; }

    /// <summary>
    /// What the asker wanted to know, or empty when they left the box blank. Already through
    /// <see cref="Services.AskQuestionPolicy"/> and moderation, and still encoded before it is placed.
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// The box reminding the reader they were sent an invitation, or empty when they don't need one.
    /// Markup from <see cref="Services.InvitationReminderService"/>, placed as it is.
    /// </summary>
    public string InvitationReminder { get; init; } = string.Empty;
}
