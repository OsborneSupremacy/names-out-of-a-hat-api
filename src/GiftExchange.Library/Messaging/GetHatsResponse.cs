namespace GiftExchange.Library.Messaging;

public record GetHatsResponse
{
    /// <summary>
    /// The name this organizer is known by, read back from their existing hats. Empty for someone
    /// who has not created one yet, which is the only case where the UI has to ask.
    /// </summary>
    public required string OrganizerName { get; init; }

    /// <summary>
    /// The requested page of hats, newest first. Empty when the page is past the end, which
    /// <see cref="TotalCount"/> tells apart from an organizer with no hats at all.
    /// </summary>
    public required ImmutableList<HatMetaData> Hats { get; init; }

    public required int Page { get; init; }

    /// <summary>Chosen by the server, so a client cannot ask for everything at once.</summary>
    public required int PageSize { get; init; }

    /// <summary>Every hat the organizer has, across all pages.</summary>
    public required int TotalCount { get; init; }
}
