namespace GiftExchange.Library.Messaging;

/// <summary>
/// The people one send is about to mail, to be remembered against the organizer who sent it.
/// </summary>
internal record RecordOrganizerSendsRequest
{
    public required string OrganizerEmail { get; init; }

    /// <summary>
    /// Everybody the send goes to. The organizer may be among them and is skipped by the provider,
    /// so callers can pass a whole hat without filtering it first.
    /// </summary>
    public required ImmutableList<OrganizerSendRecipient> Recipients { get; init; }

    public required DateTimeOffset SentAt { get; init; }
}

/// <summary>One recipient of a send: the address, and the participant it is tagged with.</summary>
internal record OrganizerSendRecipient
{
    public required Guid ParticipantId { get; init; }

    public required string Email { get; init; }
}
