namespace GiftExchange.Library.Messaging;

/// <summary>
/// One exchange the daily sweep has found something to do about, and whose it is.
/// </summary>
/// <remarks>
/// The organizer's address comes along so that the sweep can read the exchange through
/// <c>GetHatAsync</c>, which every read of a whole hat goes through and which is scoped by it.
/// </remarks>
internal record ExchangeDateSweepCandidate
{
    public required Guid HatId { get; init; }

    public required string OrganizerEmail { get; init; }
}
