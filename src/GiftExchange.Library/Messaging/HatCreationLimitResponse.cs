namespace GiftExchange.Library.Messaging;

/// <summary>
/// The limiter's answer to "may this organizer create another exchange right now?"
/// </summary>
/// <remarks>
/// The refusal comes back already worded, with its status, because the two limits behind it want
/// different ones: the daily allowance comes back on its own, so it is a 429 with a time to try
/// again, while too many open exchanges is a state the organizer has to change, so it is a 409 with
/// what to change.
/// </remarks>
internal record HatCreationLimitResponse
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
