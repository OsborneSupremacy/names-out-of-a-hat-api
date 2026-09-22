namespace GiftExchange.Library.Models;

public record Hat
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Status { get; init; }

    public required string AdditionalInformation { get; init; }

    public required string PriceRange { get; init; }

    public required Person Organizer { get; init; }

    public required ImmutableList<Participant> Participants { get; init; }

    public required DateTimeOffset InvitationsQueuedDate { get; init; }

    /// <summary>
    /// The approximate day the exchange happens, or <see cref="DateOnly.MinValue"/> when the
    /// organizer has not given one.
    /// </summary>
    public required DateOnly ExchangeDate { get; init; }
}

internal static class Hats
{
    public static Hat Empty => new()
    {
        Id = Guid.Empty,
        Name = string.Empty,
        Status = HatStatus.InProgress,
        AdditionalInformation = string.Empty,
        PriceRange = string.Empty,
        Organizer = Persons.Empty,
        Participants = [],
        InvitationsQueuedDate = DateTimeOffset.MinValue,
        ExchangeDate = DateOnly.MinValue
    };

    /// <summary>
    /// The face worn in this hat by the given person, or the empty string when they are not in it.
    /// </summary>
    /// <remarks>
    /// Looked up by address, because that is what identifies somebody within a hat. Names are not
    /// unique — two people in one exchange may answer to the same one — and a lookup by name would
    /// hand one of them the other's face.
    ///
    /// Empty rather than a stand-in face for the misses, and the misses are real: an unshaken
    /// participant has drawn nobody, and the detail view is served a draw redacted to "Hidden"
    /// until the exchange is closed. Somewhere that has no face to show should show none.
    /// </remarks>
    public static string EmojiFor(this Hat hat, Person person)
    {
        if (string.IsNullOrWhiteSpace(person.Email)) return string.Empty;

        return hat.Participants
            .Where(participant => participant.Person.Email.ContentEquals(person.Email))
            .Select(participant => participant.Emoji)
            .FirstOrDefault(string.Empty);
    }
}
