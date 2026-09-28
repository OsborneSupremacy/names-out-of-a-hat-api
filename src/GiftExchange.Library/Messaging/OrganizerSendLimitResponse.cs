namespace GiftExchange.Library.Messaging;

/// <summary>
/// The limiter's answer to "may this organizer mail these people right now?"
/// </summary>
/// <remarks>
/// Shaped like <see cref="OrganizerStandingResponse"/> and read the same way: the refusal comes
/// back already worded, with its status, so every send path refuses in the same words.
/// </remarks>
internal record OrganizerSendLimitResponse
{
    public required bool WithinLimit { get; init; }

    /// <summary>What to tell a refused organizer. Empty whenever they are within the limit.</summary>
    public required string RefusalMessage { get; init; }

    /// <summary>
    /// The status to refuse with. <see cref="HttpStatusCode.OK"/> whenever they are within the
    /// limit, which nothing should read.
    /// </summary>
    public required HttpStatusCode RefusalStatusCode { get; init; }
}
