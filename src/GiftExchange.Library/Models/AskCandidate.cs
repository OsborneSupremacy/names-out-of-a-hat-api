namespace GiftExchange.Library.Models;

/// <summary>
/// Somebody a participant can ask for gift ideas: anyone else in their exchange.
/// </summary>
/// <remarks>
/// Carries a name and an id and no address, because a page rendered from these is shown to another
/// participant. The organizer collected those addresses to send invitations with, and listing them
/// back to everybody in the exchange would be a use they were never given for.
///
/// With one exception, which <c>ParticipantNaming</c> makes: when somebody shares their name with
/// another participant, <see cref="Name"/> carries their address in brackets, because a list with
/// two identical names on it gives nobody a way to choose between them.
/// </remarks>
public record AskCandidate
{
    public required Guid ParticipantId { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Whether this is the person the asker drew. They are offered first and ticked by default,
    /// because asking your own pick is the ordinary case and everything else is a fallback for when
    /// asking them directly would give the game away.
    /// </summary>
    public required bool IsTheirPick { get; init; }
}
