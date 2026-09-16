using Amazon.SQS;
using Amazon.SQS.Model;

namespace GiftExchange.Library.Services;

/// <summary>
/// Puts a request to delete somebody's data onto the queue the deletion function reads.
/// </summary>
/// <remarks>
/// Queued rather than done in the request, because the work has no upper bound. An organizer's
/// exchanges are deleted one transaction at a time, and somebody with a few years of them can take
/// longer than API Gateway will wait.
/// </remarks>
[UsedImplicitly]
internal class DataDeletionQueue : IDataDeletionQueue
{
    private readonly IAmazonSQS _sqsClient;

    private readonly JsonService _jsonService;

    private readonly string _queueUrl;

    public DataDeletionQueue(IAmazonSQS sqsClient, JsonService jsonService)
    {
        _sqsClient = sqsClient ?? throw new ArgumentNullException(nameof(sqsClient));
        _jsonService = jsonService ?? throw new ArgumentNullException(nameof(jsonService));
        _queueUrl = EnvReader.GetStringValue("DATA_DELETION_QUEUE_URL");
    }

    public Task EnqueueAsync(DataDeletionMessage message)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = _queueUrl,
            MessageBody = _jsonService.SerializeDefault(message)
        };

        // Carries the caller's trace onto the message, for the reasons, and in the shape, that
        // EmailQueue does. Assigned as a whole dictionary: in AWS SDK for .NET v4 it starts null.
        var traceHeader = TracePropagation.CurrentTraceHeader;

        if (traceHeader is not null)
            request.MessageSystemAttributes = new Dictionary<string, MessageSystemAttributeValue>
            {
                ["AWSTraceHeader"] = new() { DataType = "String", StringValue = traceHeader }
            };

        return _sqsClient.SendMessageAsync(request);
    }
}
