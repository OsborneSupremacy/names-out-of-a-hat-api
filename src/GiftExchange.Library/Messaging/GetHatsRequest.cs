namespace GiftExchange.Library.Messaging;

public record GetHatsRequest
{
    public required string OrganizerEmail { get; init; }

    /// <summary>1-based. Taken from the <c>page</c> query string, and 1 when that is absent.</summary>
    public required int Page { get; init; }
}
