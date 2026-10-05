namespace GiftExchange.Library.Messaging;

/// <summary>
/// A signed-in participant answering an ask for gift ideas about somebody else.
/// </summary>
internal record AnswerGiftIdeaAskRequest : IParticipantScopedRequest
{
    public required string ParticipantEmail { get; init; }

    public required Guid HatId { get; init; }

    /// <summary>Which ask, from <see cref="GiftIdeaAskForYou.AskId"/>. Resolves only for the helper it was put to.</summary>
    public required Guid AskId { get; init; }

    public required string Ideas { get; init; }

    IParticipantScopedRequest IParticipantScopedRequest.WithParticipantEmail(string participantEmail) =>
        this with { ParticipantEmail = participantEmail };
}
