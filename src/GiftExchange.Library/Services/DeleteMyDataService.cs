namespace GiftExchange.Library.Services;

/// <summary>
/// Accepts a request to delete everything the caller has organized, and hands the deleting off.
/// </summary>
/// <remarks>
/// Split in two by how long each half can take. The refusal to be added anywhere is one row and is
/// written here, so it is in force before the caller is told anything — somebody who ticks that box
/// and is added to an exchange a minute later has been let down. The deletion itself has no upper
/// bound and is queued for <see cref="DataDeletionQueueHandlerService"/>.
///
/// Answers 202 rather than 204 for that reason: the work has been accepted, not done.
///
/// This removes nothing from exchanges somebody else organizes. Leaving one of those is what the
/// link in its invitation is for, and it records a refusal of that exchange as it does; deleting
/// your own data is not a way to leave everybody else's.
/// </remarks>
[UsedImplicitly]
internal class DeleteMyDataService : IApiGatewayHandler
{
    private readonly ApiGatewayAdapter _adapter;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly IDataDeletionQueue _queue;

    public DeleteMyDataService(
        ApiGatewayAdapter adapter,
        GiftExchangeProvider giftExchangeProvider,
        IDataDeletionQueue queue
    )
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context) =>
        _adapter.AdaptAsync<DeleteMyDataRequest, StatusCodeOnlyResponse>(request, ExecuteAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> ExecuteAsync(DeleteMyDataRequest request)
    {
        if (request.DoNotAddAnywhere)
            await _giftExchangeProvider
                .RecordDoNotAddAsync(new RecordDoNotAddRequest
                {
                    Email = request.OrganizerEmail,
                    // No exchange is being left, and the provider writes no exchange refusal for
                    // the empty id.
                    HatId = Guid.Empty,
                    OrganizerEmail = string.Empty,
                    BlockOrganizer = false,
                    BlockAnywhere = true
                })
                .ConfigureAwait(false);

        await _queue
            .EnqueueAsync(new DataDeletionMessage
            {
                Email = request.OrganizerEmail,
                ForgetMe = request.ForgetMe,
                RequestedAt = DateTimeOffset.UtcNow
            })
            .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(
            new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.Accepted },
            HttpStatusCode.Accepted);
    }
}
