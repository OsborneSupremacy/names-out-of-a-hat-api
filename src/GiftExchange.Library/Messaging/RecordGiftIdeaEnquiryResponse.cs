namespace GiftExchange.Library.Messaging;

/// <summary>
/// The enquiry as it stands after being recorded, whether this call is what created it or it was
/// already there.
/// </summary>
/// <remarks>
/// Deliberately does not say which of the two happened. The caller's question is not "was this new"
/// but "is there a held submission this asker has not been sent yet", and the answer to that is
/// <see cref="ReleasedAt"/> against the submission's own timestamp. A newness flag would be the
/// wrong thing to hang it on: mail here cannot report failure, so a release that was dropped has to
/// be recoverable by asking again.
/// </remarks>
internal record RecordGiftIdeaEnquiryResponse
{
    /// <summary>When they first asked.</summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>
    /// When a held submission was last passed on against this enquiry, or
    /// <see cref="DateTimeOffset.MinValue"/> if none ever has been.
    /// </summary>
    public required DateTimeOffset ReleasedAt { get; init; }
}
