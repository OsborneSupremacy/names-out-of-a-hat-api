namespace GiftExchange.Library.Messaging;

/// <summary>
/// How many distinct people have turned an organizer's mail away inside a window, and how.
/// </summary>
internal record CountOrganizerRefusalsResponse
{
    /// <summary>People who marked a message from one of this organizer's exchanges as spam.</summary>
    public required int Complaints { get; init; }

    /// <summary>
    /// People who did either that or asked, on the leave page, never to be added by this organizer
    /// again — each counted once, however many of the two they did.
    /// </summary>
    /// <remarks>
    /// A union rather than a sum. Somebody annoyed enough to complain is quite likely to have ticked
    /// the box as well, and counting them twice would suspend an organizer on the strength of one
    /// person.
    /// </remarks>
    public required int Refusals { get; init; }
}
