namespace GiftExchange.Library.Messaging;

/// <summary>
/// Everything needed to show one participant their invitation again, found from a token.
/// </summary>
internal record ViewableInvitation
{
    public required Hat Hat { get; init; }

    /// <summary>The participant the token was issued to, by the address the hat knows them by.</summary>
    public required string ParticipantEmail { get; init; }
}

internal static class ViewableInvitations
{
    internal static readonly ViewableInvitation Empty = new()
    {
        Hat = Hats.Empty,
        ParticipantEmail = string.Empty
    };
}
