namespace GiftExchange.Library.Messaging;

/// <summary>
/// A question put to the data layer on behalf of <c>OrganizerSendLimiter</c>: who has this
/// organizer mailed lately?
/// </summary>
/// <remarks>
/// The moment to count from is passed in, as it is for <see cref="CountOrganizerRefusalsRequest"/>,
/// so the window lives in the limiter and the provider only answers what it is asked.
/// </remarks>
internal record ListOrganizerRecipientsRequest
{
    public required string OrganizerEmail { get; init; }

    /// <summary>Sends at or after this moment are included; older ones are not.</summary>
    public required DateTimeOffset Since { get; init; }
}
