namespace GiftExchange.Library.Messaging;

/// <summary>
/// What one offer of gift ideas about somebody else did, for whichever page has to report it.
/// </summary>
internal record OfferResult
{
    public required OfferOutcome Outcome { get; init; }

    /// <summary>
    /// Why the text was refused. <see cref="GiftIdeaSubmissionOutcome.Shared"/> unless
    /// <see cref="Outcome"/> is <see cref="OfferOutcome.Refused"/>.
    /// </summary>
    public required GiftIdeaSubmissionOutcome Refusal { get; init; }

    /// <summary>Who the ideas are about. The all-zero id when nobody was chosen.</summary>
    public required Guid SubjectParticipantId { get; init; }

    /// <summary>Their name as the sharer should read it. Empty when nobody was chosen.</summary>
    public required string SubjectName { get; init; }

    /// <summary>
    /// When they last offered ideas about this person, on <see cref="OfferOutcome.AlreadyOffered"/>.
    /// <see cref="DateTimeOffset.MinValue"/> otherwise, and when the date could not be read back.
    /// </summary>
    public required DateTimeOffset PreviouslyOfferedAt { get; init; }
}
