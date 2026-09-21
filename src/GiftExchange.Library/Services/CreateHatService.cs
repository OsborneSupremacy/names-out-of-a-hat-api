namespace GiftExchange.Library.Services;

internal class CreateHatService : IApiGatewayHandler
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly IContentModerationService _contentModerationService;

    private readonly HatCreationLimiter _hatCreationLimiter;

    private readonly ApiGatewayAdapter _adapter;

    public CreateHatService(
        GiftExchangeProvider giftExchangeProvider,
        IContentModerationService contentModerationService,
        HatCreationLimiter hatCreationLimiter,
        ApiGatewayAdapter adapter
        )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _contentModerationService = contentModerationService ?? throw new ArgumentNullException(nameof(contentModerationService));
        _hatCreationLimiter = hatCreationLimiter ?? throw new ArgumentNullException(nameof(hatCreationLimiter));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<CreateHatRequest, CreateHatResponse>(request, CreateHatAsync);

    internal async Task<Result<CreateHatResponse>> CreateHatAsync(CreateHatRequest request)
    {
        // Validate content before processing
        var (isValid, errorMessages) = await _contentModerationService.ValidateMultipleFieldsAsync(
            new Dictionary<string, string>
            {
                ["gift exchange name"] = request.HatName,
                ["organizer name"] = request.OrganizerName
            });

        if (!isValid)
            return new Result<CreateHatResponse>(
                new InvalidOperationException(string.Join(" ", errorMessages)),
                HttpStatusCode.BadRequest
            );

        var (hatExists, _ ) = await _giftExchangeProvider
            .DoesHatAlreadyExistAsync(request.OrganizerEmail, request.HatName)
            .ConfigureAwait(false);

        if (hatExists)
            return new Result<CreateHatResponse>(new InvalidOperationException("A gift exchange with this name already exists. If this is the same gift exchange for a different year, try adding the year in the name to differentiate it from previous exchanges."), HttpStatusCode.Conflict);

        // After the duplicate name rather than before it, so that a request which was never going
        // to create anything is answered with the reason it was not, instead of a limit it did not
        // reach.
        var limit = await _hatCreationLimiter
            .CheckAsync(request.OrganizerEmail)
            .ConfigureAwait(false);

        if (!limit.WithinLimit)
            return new Result<CreateHatResponse>(
                new InvalidOperationException(limit.RefusalMessage),
                limit.RefusalStatusCode);

        var newHat = new HatDataModel
        {
            HatId = Guid.NewGuid(),
            HatName = request.HatName,
            Status = HatStatus.InProgress,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            OrganizerEmail = request.OrganizerEmail,
            OrganizerName = request.OrganizerName
        };

        var created = await _giftExchangeProvider
            .CreateHatAsync(newHat)
            .ConfigureAwait(false);

        if(!created)
            return new Result<CreateHatResponse>(new CreateHatResponse { HatId = newHat.HatId }, HttpStatusCode.OK);

        await _giftExchangeProvider
            .CreateParticipantAsync(new AddParticipantRequest
            {
                HatId = newHat.HatId,
                OrganizerEmail = request.OrganizerEmail,
                Name = request.OrganizerName,
                Email = request.OrganizerEmail
            }, []);

        return new Result<CreateHatResponse>(new CreateHatResponse { HatId = newHat.HatId }, HttpStatusCode.Created);
    }
}
