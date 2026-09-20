namespace GiftExchange.Library.Messaging;

/// <summary>
/// What a participant most recently shared about themselves, and the two things the share page has
/// to know about it before it can describe it honestly.
/// </summary>
internal record GetLatestGiftIdeaResponse
{
    /// <summary>Their words, or the empty string if they have shared nothing.</summary>
    public required string Ideas { get; init; }

    /// <summary>
    /// Whether <see cref="Ideas"/> is being held back until somebody asks. The newest submission's
    /// own answer, not a summary of every one they have made: sharing outright after holding
    /// something back replaces what is held.
    /// </summary>
    public required bool HoldUntilAsked { get; init; }

    /// <summary>
    /// When it was written, or <see cref="DateTimeOffset.MinValue"/> if they have shared nothing.
    /// </summary>
    /// <remarks>
    /// Compared against <see cref="GiftIdeaEnquiryEntity.ReleasedAt"/> to decide whether a held
    /// submission is owed to somebody who asked. That comparison, rather than a flag saying an
    /// enquiry was new, is what makes a release that was dropped recoverable: the mail sender here
    /// cannot report a failure.
    /// </remarks>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Whether any submission of theirs has ever been sent outright.
    /// </summary>
    /// <remarks>
    /// The form promises that held ideas will never be seen by anybody unless they are asked for,
    /// and for somebody who has already shared once without holding back, that promise is not true
    /// of what they sent earlier — nothing can recall an email. This is what lets the page say so.
    ///
    /// Read from their own submissions and never from an enquiry, which would leak that their giver
    /// had asked.
    /// </remarks>
    public required bool HasSharedOutrightBefore { get; init; }
}
