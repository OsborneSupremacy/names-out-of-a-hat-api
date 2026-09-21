namespace GiftExchange.Library.Messaging;

/// <summary>
/// One participant's unprompted offer of ideas about another, put to the throttle.
/// </summary>
/// <remarks>
/// Held per pair for the reason <see cref="ReserveAskSlotRequest"/> gives, and keyed apart from the
/// Ask on purpose. Being asked about somebody and volunteering about them are different acts by
/// different people, and a shared slot would let either silently suppress the other.
///
/// The pair is the sharer and the <em>subject</em>, never the sharer and whoever receives the mail.
/// That is a leak, not a preference: an organizer editing picks can leave one participant holding
/// two names, so a slot keyed on the recipient would refuse an offer about Liz because of an
/// earlier offer about Katie — telling the sharer that the same person drew both, which is exactly
/// what this application never says. Keyed on the subject, the throttle's behaviour is a function
/// of what the sharer already knows: who they wrote about, and when.
/// </remarks>
internal record ReserveOfferSlotRequest
{
    public required Guid SharerParticipantId { get; init; }

    /// <summary>Who the ideas are about.</summary>
    public required Guid SubjectParticipantId { get; init; }

    public required TimeSpan Window { get; init; }
}
