namespace GiftExchange.Library.Abstractions;

/// <summary>
/// Where a request to delete somebody's data is handed off to be carried out.
/// </summary>
internal interface IDataDeletionQueue
{
    public Task EnqueueAsync(DataDeletionMessage message);
}
