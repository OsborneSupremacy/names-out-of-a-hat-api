namespace GiftExchange.Library.Messaging;

/// <summary>
/// One page of somebody's participations, as <c>GiftExchangeProvider.GetParticipatingHatsAsync</c>
/// reads it.
/// </summary>
internal record GetParticipatingHatsPageRequest
{
    public required string ParticipantEmail { get; init; }

    /// <summary>1-based.</summary>
    public required int Page { get; init; }

    public required int PageSize { get; init; }
}
