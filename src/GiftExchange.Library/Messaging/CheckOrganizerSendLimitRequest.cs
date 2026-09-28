namespace GiftExchange.Library.Messaging;

/// <summary>
/// The question put to <c>OrganizerSendLimiter</c>: may this organizer mail these people now?
/// </summary>
internal record CheckOrganizerSendLimitRequest
{
    public required string OrganizerEmail { get; init; }

    /// <summary>
    /// Everybody the send would go to. The organizer may be among them, and does not count.
    /// </summary>
    public required ImmutableList<string> RecipientEmails { get; init; }
}
