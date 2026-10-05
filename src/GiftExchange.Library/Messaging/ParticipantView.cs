namespace GiftExchange.Library.Messaging;

/// <summary>
/// A gift exchange as one of the people in it sees it: read-only, and holding nothing the organizer
/// alone should have.
/// </summary>
/// <remarks>
/// A type of its own rather than <see cref="Hat"/> with parts taken out. <see cref="Hat"/> is the
/// organizer's record -- every address, the eligibility rules, what SES said about each email -- and
/// a view built by subtracting from it would leak whatever is added to it next. This one is built by
/// adding, so a new field reaches participants only when somebody puts it here.
///
/// Nobody's address is here, except where two people share a name and the address is the only way
/// to tell them apart; that is the rule <see cref="Services.ParticipantNaming"/> applies to the
/// invitations these same people have already been sent.
/// </remarks>
public record ParticipantView
{
    public required Guid HatId { get; init; }

    public required string Name { get; init; }

    /// <summary>Decides whether everybody's pick is shown, or only the caller's.</summary>
    public required string Status { get; init; }

    public required string OrganizerName { get; init; }

    public required string AdditionalInformation { get; init; }

    public required string PriceRange { get; init; }

    /// <summary><see cref="DateOnly.MinValue"/> when the organizer has not given one.</summary>
    public required DateOnly ExchangeDate { get; init; }

    /// <summary>Everybody in the exchange, the caller included, by name.</summary>
    public required ImmutableList<ParticipantViewEntry> Participants { get; init; }

    /// <summary>
    /// Whether the caller may take themselves out of this exchange. False for the organizer, who is
    /// never offered a way to leave their own exchange — they can remove themselves, or delete it.
    /// </summary>
    public required bool CanLeave { get; init; }

    /// <summary>What the caller may see and do about gift ideas in this exchange.</summary>
    public required ParticipantGiftIdeas GiftIdeas { get; init; }
}

public record ParticipantViewEntry
{
    /// <summary>The name to show, disambiguated as <see cref="Services.ParticipantNaming"/> does.</summary>
    public required string Name { get; init; }

    public required string Emoji { get; init; }

    /// <summary>Whether this is the person asking.</summary>
    public required bool IsYou { get; init; }

    /// <summary>
    /// Who they are giving to, by the same kind of name. Empty when that is not the caller's to
    /// know: anybody else's pick before the exchange closes.
    /// </summary>
    public required string PickedRecipient { get; init; }
}

internal static class ParticipantViews
{
    /// <summary>What comes back when there is no such exchange for this caller. Never serialized.</summary>
    public static ParticipantView Empty => new()
    {
        HatId = Guid.Empty,
        Name = string.Empty,
        Status = HatStatus.InProgress,
        OrganizerName = string.Empty,
        AdditionalInformation = string.Empty,
        PriceRange = string.Empty,
        ExchangeDate = DateOnly.MinValue,
        Participants = [],
        CanLeave = false,
        GiftIdeas = ParticipantGiftIdeasDefaults.Empty
    };
}
