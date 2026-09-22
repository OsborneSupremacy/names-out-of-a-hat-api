namespace GiftExchange.Library.Services;

internal class EditHatService : IApiGatewayHandler
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly ApiGatewayAdapter _adapter;

    public EditHatService(
        GiftExchangeProvider giftExchangeProvider,
        HatPreconditionValidator hatPreconditionValidator,
        ApiGatewayAdapter adapter
        )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<EditHatRequest, StatusCodeOnlyResponse>(request, EditHatAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> EditHatAsync(EditHatRequest request)
    {
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = new Dictionary<string, string>
                {
                    ["gift exchange name"] = request.Name,
                    ["additional information"] = request.AdditionalInformation,
                    ["price range"] = request.PriceRange
                },
                ValidHatStatuses = [HatStatus.InProgress, HatStatus.ReadyForAssignment, HatStatus.NamesAssigned]
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<StatusCodeOnlyResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode);

        // A date that has already passed may be kept -- an exchange set up in good time and then
        // left alone still has to have its price range edited -- but not newly set. The previous
        // value is the one the precondition check just read.
        if (request.ExchangeDate != hatPreconditionResult.Hat.ExchangeDate
            && ExchangeDates.HasPassed(request.ExchangeDate, DateTimeOffset.UtcNow))
            return new Result<StatusCodeOnlyResponse>(
                new InvalidOperationException("The exchange date can't be in the past."),
                HttpStatusCode.BadRequest);

        await _giftExchangeProvider.EditHatAsync(request)
            .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.OK}, HttpStatusCode.OK);
    }
}
