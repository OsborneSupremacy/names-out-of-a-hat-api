namespace GiftExchange.Library.Messaging;

[UsedImplicitly]
public record EditHatRequest : IOrganizerScopedRequest
{
    public required Guid HatId { get; init; }

    public required string OrganizerEmail { get; init; }

    public required string Name { get; init; }

    public required string AdditionalInformation { get; init; }

    public required string PriceRange { get; init; }

    /// <summary>
    /// The approximate day of the exchange, or <see cref="DateOnly.MinValue"/> to say there is
    /// none. Required like every other field here: an edit states the whole of what it edits.
    /// </summary>
    public required DateOnly ExchangeDate { get; init; }

    IOrganizerScopedRequest IOrganizerScopedRequest.WithOrganizerEmail(string organizerEmail) =>
        this with { OrganizerEmail = organizerEmail };
}
