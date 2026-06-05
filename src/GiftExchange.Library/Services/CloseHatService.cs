namespace GiftExchange.Library.Services;

internal class CloseHatService : IApiGatewayHandler
{
    private readonly ILogger<AssignRecipientsService> _logger;

    private readonly ApiGatewayAdapter _adapter;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    public CloseHatService(
        ILogger<AssignRecipientsService> logger,
        ApiGatewayAdapter adapter,
        GiftExchangeProvider giftExchangeProvider,
        HatPreconditionValidator hatPreconditionValidator
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<CloseHatRequest, StatusCodeOnlyResponse>(request, CloseHatAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> CloseHatAsync(
        CloseHatRequest request
    )
    {
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = [],
                ValidHatStatuses =
                [
                    HatStatus.CoolingOff
                ]
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<StatusCodeOnlyResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode);

        await _giftExchangeProvider
            .UpdateHatStatusAsync(request.OrganizerEmail, request.HatId, HatStatus.Closed)
            .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.OK}, HttpStatusCode.OK);
    }
}
