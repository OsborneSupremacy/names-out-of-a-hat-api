namespace GiftExchange.Library.DataModels;

public record HatDataModel
{
    public required string OrganizerEmail { get; init; }

    public required Guid HatId { get; init; }

    public required string OrganizerName { get; init; }

    public required string HatName { get; init; }

    public required string Status { get; init; }

    public required string AdditionalInformation { get; init; }

    public required string PriceRange { get; init; }

    /// <summary><see cref="DateOnly.MinValue"/> when the organizer has not given one.</summary>
    public required DateOnly ExchangeDate { get; init; }
}
