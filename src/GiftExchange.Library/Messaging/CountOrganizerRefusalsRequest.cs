namespace GiftExchange.Library.Messaging;

/// <summary>
/// A question put to the data layer on behalf of <c>OrganizerStandingChecker</c>: how many people
/// have lately said they did not want this organizer's mail?
/// </summary>
/// <remarks>
/// The moment to count from is passed in, as it is for <see cref="CountHatsCreatedSinceRequest"/>,
/// so the window lives in the checker and the provider only answers what it is asked.
/// </remarks>
internal record CountOrganizerRefusalsRequest
{
    public required string OrganizerEmail { get; init; }

    /// <summary>Complaints and refusals at or after this moment are counted; older ones are not.</summary>
    public required DateTimeOffset Since { get; init; }
}
