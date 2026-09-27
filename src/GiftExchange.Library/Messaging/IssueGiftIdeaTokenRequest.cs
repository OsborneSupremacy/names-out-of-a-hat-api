namespace GiftExchange.Library.Messaging;

/// <summary>
/// One more gift ideas token for a single participant, alongside any they already hold.
/// </summary>
internal record IssueGiftIdeaTokenRequest
{
    public required Guid ParticipantId { get; init; }

    /// <summary>
    /// Whether a button pressed with this token will count as proof that its holder has seen their
    /// invitation. See <see cref="Entities.GiftIdeaTokenEntity.ProvesInvitationSeen"/>.
    /// </summary>
    /// <remarks>
    /// Required rather than defaulted, so that every caller has to say which kind of email the token
    /// is going into. Getting it wrong in one direction hides the reminder from somebody who never
    /// saw their invitation, which is the one person it exists for.
    /// </remarks>
    public required bool ProvesInvitationSeen { get; init; }
}
