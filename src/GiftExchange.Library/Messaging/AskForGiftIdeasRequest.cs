namespace GiftExchange.Library.Messaging;

/// <summary>
/// A signed-in participant asking for gift ideas about their pick.
/// </summary>
internal record AskForGiftIdeasRequest : IParticipantScopedRequest
{
    public required string ParticipantEmail { get; init; }

    public required Guid HatId { get; init; }

    /// <summary>
    /// Who to ask, from <see cref="AskCandidate.ParticipantId"/>. Each survives only if the database
    /// agrees it belongs to the caller's exchange.
    /// </summary>
    public required ImmutableList<Guid> ParticipantIds { get; init; }

    /// <summary>Optional. Sent to everybody asked, without the caller's name.</summary>
    public required string Question { get; init; }

    IParticipantScopedRequest IParticipantScopedRequest.WithParticipantEmail(string participantEmail) =>
        this with { ParticipantEmail = participantEmail };
}
