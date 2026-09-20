namespace GiftExchange.Library.Messaging;

/// <summary>
/// One participant's asking for gift ideas about their own pick, on its way to being written down.
/// </summary>
/// <remarks>
/// Both ids, and neither is derivable from the other here: the asker's pick is read once when the
/// Ask page resolves the link, and the row has to name the pick as it stood then rather than as it
/// stands whenever a held submission is next looked at.
/// </remarks>
internal record RecordGiftIdeaEnquiryRequest
{
    public required Guid AskerParticipantId { get; init; }

    /// <summary>Who they asked about: the participant whose name they drew.</summary>
    public required Guid SubjectParticipantId { get; init; }
}
