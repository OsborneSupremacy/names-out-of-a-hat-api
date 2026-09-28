namespace GiftExchange.Library.Messaging;

/// <summary>
/// How many distinct addresses have hard-bounced an organizer's mail inside a window, against how
/// many they mailed.
/// </summary>
internal record CountOrganizerBouncesResponse
{
    /// <summary>Distinct addresses that bounced permanently.</summary>
    public required int Bounced { get; init; }

    /// <summary>Distinct addresses mailed, from <c>organizer_send</c>.</summary>
    /// <remarks>
    /// Can be smaller than <see cref="Bounced"/> for mail sent before the ledger existed, whose
    /// bounces were attributed through the participant row instead. The checker allows for that.
    /// </remarks>
    public required int Recipients { get; init; }
}
