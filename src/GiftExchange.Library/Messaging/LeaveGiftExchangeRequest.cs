namespace GiftExchange.Library.Messaging;

/// <summary>
/// A signed-in participant taking themselves out of a gift exchange.
/// </summary>
/// <remarks>
/// The two refusals are the ones the leave page in an email offers, and like them they only ever add
/// a refusal. Leaving always refuses this exchange; these widen it.
/// </remarks>
internal record LeaveGiftExchangeRequest : IParticipantScopedRequest
{
    public required string ParticipantEmail { get; init; }

    public required Guid HatId { get; init; }

    /// <summary>Don't let this organizer add the caller to any gift exchange again.</summary>
    public required bool BlockOrganizer { get; init; }

    /// <summary>Don't let anybody add the caller to a gift exchange.</summary>
    public required bool BlockAnywhere { get; init; }

    IParticipantScopedRequest IParticipantScopedRequest.WithParticipantEmail(string participantEmail) =>
        this with { ParticipantEmail = participantEmail };
}
