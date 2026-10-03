namespace GiftExchange.Library.Messaging;

public record GetParticipatingHatsResponse
{
    /// <summary>
    /// The requested page of exchanges, most recently invited first. Empty when the page is past
    /// the end, which <see cref="TotalCount"/> tells apart from somebody in no exchanges at all.
    /// </summary>
    public required ImmutableList<ParticipatingHatMetaData> Hats { get; init; }

    public required int Page { get; init; }

    /// <summary>Chosen by the server, so a client cannot ask for everything at once.</summary>
    public required int PageSize { get; init; }

    /// <summary>Every exchange they can see as a participant, across all pages.</summary>
    public required int TotalCount { get; init; }
}
