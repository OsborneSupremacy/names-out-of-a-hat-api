namespace GiftExchange.Library.Messaging;

/// <summary>
/// What <c>GiftExchangeProvider.ExportHatAsync</c> found: the exchange, and whether there was one.
/// </summary>
/// <remarks>
/// A missing hat and an empty one are different answers, and an <see cref="ExportedHat"/> on its own
/// cannot tell them apart — the all-zero id it would carry is the sentinel hat's, which is a real
/// row. So the question is answered separately from the thing it is about.
/// </remarks>
internal record ExportHatDataResponse
{
    public required bool Exists { get; init; }

    public required ExportedHat Hat { get; init; }
}

internal static class ExportedHats
{
    /// <summary>What comes back when there is no such exchange. Never serialized.</summary>
    public static ExportedHat Empty => new()
    {
        HatId = Guid.Empty,
        Name = string.Empty,
        Status = HatStatus.InProgress,
        AdditionalInformation = string.Empty,
        PriceRange = string.Empty,
        CreatedAt = DateTimeOffset.MinValue,
        InvitationsQueuedAt = DateTimeOffset.MinValue,
        CopiedFromHatId = Guid.Empty,
        ExchangeDate = DateOnly.MinValue,
        Organizer = new ExportedPerson
        {
            PersonId = Guid.Empty,
            Name = string.Empty,
            Email = string.Empty
        },
        Participants = []
    };
}
