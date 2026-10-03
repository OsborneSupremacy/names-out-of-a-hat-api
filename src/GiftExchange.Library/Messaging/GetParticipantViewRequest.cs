namespace GiftExchange.Library.Messaging;

/// <summary>
/// One gift exchange, as somebody taking part in it sees it.
/// </summary>
/// <remarks>
/// Deliberately not an <see cref="IOrganizerScopedRequest"/>: the caller is a participant, and the
/// exchange is found by their place in it rather than by who owns it.
/// </remarks>
internal record GetParticipantViewRequest
{
    /// <summary>The authenticated caller. Never read from the path.</summary>
    public required string ParticipantEmail { get; init; }

    public required Guid HatId { get; init; }
}
