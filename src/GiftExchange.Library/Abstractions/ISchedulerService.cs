namespace GiftExchange.Library.Abstractions;

internal interface ISchedulerService
{
    public Task CreateCooledOffScheduleAsync(
        SendInvitationsRequest request,
        DateTimeOffset invitationsQueuedAt
    );
}
