namespace GiftExchange.Library.Messaging;

public record GetHatsPageResponse
{
    /// <summary>
    /// The name this organizer is known by. Empty for somebody the application has never seen.
    /// </summary>
    public required string OrganizerName { get; init; }

    /// <summary>The requested page only. Empty when the page is past the end.</summary>
    public required ImmutableList<HatMetaData> Hats { get; init; }

    /// <summary>Every hat the organizer has, across all pages.</summary>
    public required int TotalCount { get; init; }
}
