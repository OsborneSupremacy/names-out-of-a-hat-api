namespace GiftExchange.Library.Messaging;

/// <summary>
/// A held submission having been passed on to the person who asked for ideas.
/// </summary>
/// <remarks>
/// Stamped after the email has been handed to the sender rather than before, so a release that never
/// went out is tried again by the next ask.
/// </remarks>
internal record MarkGiftIdeaEnquiryReleasedRequest
{
    public required Guid AskerParticipantId { get; init; }

    public required Guid SubjectParticipantId { get; init; }

    /// <summary>
    /// When it was passed on. Stated by the caller rather than read from the clock here, so that the
    /// stamp and the submission it is being compared against are read the same way.
    /// </summary>
    public required DateTimeOffset ReleasedAt { get; init; }
}
