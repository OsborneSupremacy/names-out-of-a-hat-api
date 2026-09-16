using Amazon.Lambda.SQSEvents;

namespace GiftExchange.Library.Services;

/// <summary>
/// Carries out one queued request to delete somebody's data.
/// </summary>
/// <remarks>
/// Failures are thrown, not logged and swallowed. Every step of the deletion is safe to repeat, so
/// letting SQS redeliver is the retry, and a message that keeps failing ends on the dead-letter
/// queue where the alarm will find it. Swallowing one would leave data somebody was told was being
/// deleted with nothing recording that it was not.
/// </remarks>
[UsedImplicitly]
internal class DataDeletionQueueHandlerService
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly JsonService _jsonService;

    private readonly ILogger<DataDeletionQueueHandlerService> _logger;

    public DataDeletionQueueHandlerService(
        GiftExchangeProvider giftExchangeProvider,
        JsonService jsonService,
        ILogger<DataDeletionQueueHandlerService> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _jsonService = jsonService ?? throw new ArgumentNullException(nameof(jsonService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ProcessRecordAsync(SQSEvent.SQSMessage record, ILambdaContext context)
    {
        // The body is not echoed into the exception: it holds the address being forgotten.
        var message = _jsonService.DeserializeDefault<DataDeletionMessage>(record.Body)
                      ?? throw new InvalidOperationException($"Invalid data deletion message {record.MessageId}.");

        await _giftExchangeProvider
            .DeleteMyDataAsync(message)
            .ConfigureAwait(false);

        _logger.LogInformation("Data deletion message {MessageId} processed.", record.MessageId);
    }
}
