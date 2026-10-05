namespace GiftExchange.Library.Messaging;

/// <summary>
/// A signed-in participant writing their own gift ideas, for whoever drew them.
/// </summary>
internal record ShareGiftIdeasRequest : IParticipantScopedRequest
{
    public required string ParticipantEmail { get; init; }

    public required Guid HatId { get; init; }

    public required string Ideas { get; init; }

    /// <summary>Keep them until whoever drew the caller asks for gift ideas, rather than sending now.</summary>
    public required bool HoldUntilAsked { get; init; }

    IParticipantScopedRequest IParticipantScopedRequest.WithParticipantEmail(string participantEmail) =>
        this with { ParticipantEmail = participantEmail };
}
