namespace GiftExchange.Library.Messaging;

/// <summary>
/// A complaint to remember against whoever organizes the exchange a participant belongs to.
/// </summary>
/// <remarks>
/// Carries the participant rather than the organizer because that is all a delivery event knows.
/// The provider resolves the rest, and has to do it now: once the participant or the exchange is
/// gone, nothing leads from the complaint back to the organizer.
/// </remarks>
internal record RecordOrganizerComplaintRequest
{
    /// <summary>The participant the complained-about message was addressed to.</summary>
    public required Guid ParticipantId { get; init; }

    /// <summary>The address that complained, as SES reported it.</summary>
    public required string Email { get; init; }

    public required DateTimeOffset ComplainedAt { get; init; }
}
