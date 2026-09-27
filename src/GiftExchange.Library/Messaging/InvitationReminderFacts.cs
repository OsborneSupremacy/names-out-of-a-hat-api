namespace GiftExchange.Library.Messaging;

/// <summary>
/// What a follow-up email needs to know to remind somebody they were sent an invitation.
/// </summary>
internal record InvitationReminderFacts
{
    /// <summary>
    /// False when there is nothing to remind them of — the exchange is not in a state where an
    /// invitation stands, or they are its organizer — and false when they have plainly seen it
    /// already, by pressing a button on a page only their invitation could have led them to.
    /// </summary>
    public required bool IsNeeded { get; init; }

    public required string OrganizerName { get; init; }

    public required string HatName { get; init; }

    /// <summary>
    /// When this participant's invitation was sent, as near as is known: SES's own time for the
    /// latest invitation to them, or when the exchange's invitations were queued if SES has not
    /// reported on it.
    /// </summary>
    public required DateTimeOffset SentAt { get; init; }
}

internal static class InvitationReminders
{
    internal static readonly InvitationReminderFacts NotNeeded = new()
    {
        IsNeeded = false,
        OrganizerName = string.Empty,
        HatName = string.Empty,
        SentAt = DateTimeOffset.MinValue
    };
}
