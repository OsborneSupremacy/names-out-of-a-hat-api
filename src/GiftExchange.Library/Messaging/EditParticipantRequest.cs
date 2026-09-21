namespace GiftExchange.Library.Messaging;

[UsedImplicitly]
public record EditParticipantRequest : IOrganizerScopedRequest
{
    public required string OrganizerEmail { get; init; }

    public required Guid HatId { get; init; }

    public required string Email { get; init; }

    /// <summary>
    /// The email addresses of the participants this one may draw.
    /// </summary>
    /// <remarks>
    /// Addresses rather than names, because two participants in one exchange may share a name.
    /// </remarks>
    // ReSharper disable once CollectionNeverUpdated.Global
    public required ImmutableList<string> EligibleRecipients { get; init; }

    IOrganizerScopedRequest IOrganizerScopedRequest.WithOrganizerEmail(string organizerEmail) =>
        this with { OrganizerEmail = organizerEmail };
}
