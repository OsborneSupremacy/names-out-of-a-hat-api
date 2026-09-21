namespace GiftExchange.Library.Messaging;

/// <summary>
/// What became of an attempt to move one participant onto a different email address.
/// </summary>
internal record UpdateParticipantAddressResponse
{
    public required AddressChangeOutcome Outcome { get; init; }

    /// <summary>
    /// The participant that was moved, or the all-zero id when nothing was.
    /// </summary>
    /// <remarks>
    /// The same row before and after — that is the point of the operation. Everything hanging off a
    /// participant is keyed by this id, so keeping it is what preserves the pick, the eligibility
    /// and the delivery history that removing and re-adding somebody would destroy.
    /// </remarks>
    public required Guid ParticipantId { get; init; }

    /// <summary>
    /// The name the participant goes by now.
    /// </summary>
    /// <remarks>
    /// Usually unchanged, and worth returning anyway. A name belongs to a person rather than to a
    /// participant, so moving somebody onto an address that already belongs to one adopts that
    /// person's name — and the email composed next has to greet them by the name they now have.
    /// Empty on a refusal.
    /// </remarks>
    public required string Name { get; init; }
}

internal static class UpdateParticipantAddressResponses
{
    /// <summary>Nobody was moved. The shape every failure starts from.</summary>
    public static UpdateParticipantAddressResponse For(AddressChangeOutcome outcome) =>
        new()
        {
            Outcome = outcome,
            ParticipantId = Guid.Empty,
            Name = string.Empty
        };
}
