namespace GiftExchange.Library.Messaging;

internal record GetParticipatingHatsPageResponse
{
    /// <summary>The requested page only. Empty when the page is past the end.</summary>
    public required ImmutableList<ParticipatingHatMetaData> Hats { get; init; }

    /// <summary>Every exchange they can see as a participant, across all pages.</summary>
    public required int TotalCount { get; init; }
}
