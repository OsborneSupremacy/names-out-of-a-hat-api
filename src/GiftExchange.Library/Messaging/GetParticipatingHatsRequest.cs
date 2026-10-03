namespace GiftExchange.Library.Messaging;

/// <summary>
/// One page of the gift exchanges the caller is taking part in, as opposed to organizing.
/// </summary>
public record GetParticipatingHatsRequest
{
    /// <summary>The authenticated caller. Never read from the path.</summary>
    public required string ParticipantEmail { get; init; }

    /// <summary>1-based. Taken from the <c>page</c> query string, and 1 when that is absent.</summary>
    public required int Page { get; init; }
}
