namespace GiftExchange.Library.Messaging;

/// <summary>
/// One page of an organizer's gift exchanges, as <c>GiftExchangeProvider.GetHatsAsync</c> reads it.
/// </summary>
public record GetHatsPageRequest
{
    public required string OrganizerEmail { get; init; }

    /// <summary>1-based.</summary>
    public required int Page { get; init; }

    public required int PageSize { get; init; }
}
