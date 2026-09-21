namespace GiftExchange.Library.Messaging;

/// <summary>
/// The subject somebody chose on the offer page, put back to the database to be checked.
/// </summary>
/// <remarks>
/// All three ids, because the check is the point rather than the lookup. The chosen id arrives in a
/// form submission, and a form this application rendered is still something the sender can edit
/// before posting it back — so who they are and which exchange they are in are supplied here
/// alongside it rather than taken on trust from the page.
/// </remarks>
internal record FindOfferTargetRequest
{
    public required Guid HatId { get; init; }

    /// <summary>Who is offering. Never the subject, and never the recipient.</summary>
    public required Guid SharerParticipantId { get; init; }

    /// <summary>Who they say the ideas are about.</summary>
    public required Guid SubjectParticipantId { get; init; }
}
