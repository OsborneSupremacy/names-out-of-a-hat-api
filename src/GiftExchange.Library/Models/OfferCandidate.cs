namespace GiftExchange.Library.Models;

/// <summary>
/// Somebody a participant can offer gift ideas about: anyone else in their exchange but their own
/// pick.
/// </summary>
/// <remarks>
/// Their own pick is left out rather than marked, which is the one way this differs from
/// <see cref="AskCandidate"/>. Ideas about your own pick would be routed back to you, so the choice
/// is not one to offer and explain — it is not a choice at all. Nothing is given away by the
/// omission: the sharer is the one person who already knows both names missing from the list.
///
/// Carries a name and an id and no address, for the reason <see cref="AskCandidate"/> gives: a page
/// rendered from these is shown to another participant, and the organizer collected those addresses
/// to send invitations with.
/// </remarks>
public record OfferCandidate
{
    public required Guid ParticipantId { get; init; }

    public required string Name { get; init; }
}
