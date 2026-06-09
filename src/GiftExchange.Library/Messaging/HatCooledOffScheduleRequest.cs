namespace GiftExchange.Library.Messaging;

public record HatCooledOffScheduleRequest
{
    public required Guid HatId { get; init; }

    public required string OrganizerEmail { get; init; }
}
