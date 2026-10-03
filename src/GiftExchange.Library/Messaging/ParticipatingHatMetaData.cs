namespace GiftExchange.Library.Messaging;

/// <summary>
/// One line in the list of exchanges somebody is taking part in.
/// </summary>
/// <remarks>
/// Not <see cref="HatMetaData"/>, although it overlaps: a participant did not create the exchange,
/// so it is named by who did, and when it happens matters to them more than when its status last
/// moved.
/// </remarks>
public record ParticipatingHatMetaData
{
    public required Guid HatId { get; init; }

    public required string HatName { get; init; }

    public required string OrganizerName { get; init; }

    public required string Status { get; init; }

    /// <summary><see cref="DateOnly.MinValue"/> when the organizer has not given one.</summary>
    public required DateOnly ExchangeDate { get; init; }
}
