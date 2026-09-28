namespace GiftExchange.Library.Messaging;

/// <summary>
/// A hard bounce to remember against whoever organizes the exchange a participant belongs to.
/// </summary>
/// <remarks>
/// Carries the participant rather than the organizer for the reason
/// <see cref="RecordOrganizerComplaintRequest"/> gives.
/// </remarks>
internal record RecordOrganizerBounceRequest
{
    /// <summary>The participant the bounced message was addressed to.</summary>
    public required Guid ParticipantId { get; init; }

    /// <summary>The address that bounced, as SES reported it.</summary>
    public required string Email { get; init; }

    public required DateTimeOffset BouncedAt { get; init; }
}
