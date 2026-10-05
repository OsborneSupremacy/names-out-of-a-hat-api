namespace GiftExchange.Library.Messaging;

/// <summary>
/// A signed-in participant offering gift ideas about somebody other than their pick, unasked.
/// </summary>
internal record OfferGiftIdeasRequest : IParticipantScopedRequest
{
    public required string ParticipantEmail { get; init; }

    public required Guid HatId { get; init; }

    /// <summary>From <see cref="OfferCandidate.ParticipantId"/>. Checked against the database, not trusted.</summary>
    public required Guid SubjectParticipantId { get; init; }

    public required string Ideas { get; init; }

    IParticipantScopedRequest IParticipantScopedRequest.WithParticipantEmail(string participantEmail) =>
        this with { ParticipantEmail = participantEmail };
}
