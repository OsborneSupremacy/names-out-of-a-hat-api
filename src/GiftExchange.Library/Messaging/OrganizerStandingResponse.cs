namespace GiftExchange.Library.Messaging;

/// <summary>
/// The checker's answer to "may this organizer send mail to participants right now?"
/// </summary>
/// <remarks>
/// Shaped like <see cref="HatCreationLimitResponse"/>, and read the same way: the refusal comes
/// back already worded, with its status, so every send path refuses in the same words.
/// </remarks>
internal record OrganizerStandingResponse
{
    public required bool MaySend { get; init; }

    /// <summary>What to tell a refused organizer. Empty whenever they may send.</summary>
    public required string RefusalMessage { get; init; }

    /// <summary>
    /// The status to refuse with. <see cref="HttpStatusCode.OK"/> whenever they may send, which
    /// nothing should read.
    /// </summary>
    public required HttpStatusCode RefusalStatusCode { get; init; }
}
