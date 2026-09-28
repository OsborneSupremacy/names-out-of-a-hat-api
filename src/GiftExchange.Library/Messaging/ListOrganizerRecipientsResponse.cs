namespace GiftExchange.Library.Messaging;

/// <summary>
/// The distinct people an organizer has mailed inside a window, and when each was last mailed.
/// </summary>
/// <remarks>
/// The last send rather than the first, because that is the one that keeps an address inside the
/// window: somebody mailed on Monday and again on Friday is still one of this week's recipients on
/// the following Tuesday.
/// </remarks>
internal record ListOrganizerRecipientsResponse
{
    /// <summary>Latest send by address, lower-cased and trimmed.</summary>
    public required ImmutableDictionary<string, DateTimeOffset> LastSentAt { get; init; }
}
