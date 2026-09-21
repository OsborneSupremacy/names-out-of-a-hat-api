namespace GiftExchange.Library.Services;

internal class RemoveParticipantService : IApiGatewayHandler
{
    ILogger<RemoveParticipantService> _logger;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly ApiGatewayAdapter _adapter;

    public RemoveParticipantService(
        ILogger<RemoveParticipantService> logger,
        GiftExchangeProvider giftExchangeProvider,
        HatPreconditionValidator hatPreconditionValidator,
        ApiGatewayAdapter adapter
        )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context) =>
        _adapter.AdaptAsync<RemoveParticipantRequest, StatusCodeOnlyResponse>(request, ExecuteAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> ExecuteAsync(RemoveParticipantRequest request)
    {
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = [],
                ValidHatStatuses = [
                    HatStatus.InProgress,
                    HatStatus.ReadyForAssignment,
                    HatStatus.NamesAssigned
                ]
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<StatusCodeOnlyResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode);

        var hat = hatPreconditionResult.Hat;

        if(request.Email.ContentEquals(hat.Organizer.Email))
            return new Result<StatusCodeOnlyResponse>(new InvalidOperationException("The organizer cannot be removed."), HttpStatusCode.BadRequest);

        var participant = hat.Participants
            .FirstOrDefault(p => p.Person.Email.ContentEquals(request.Email));

        if(participant is null)
            return new Result<StatusCodeOnlyResponse>(new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.NoContent }, HttpStatusCode.NoContent);

        // remove participant from eligible recipients of all other participants
        await _giftExchangeProvider
            .RemoveParticipantFromEligibleRecipientsAsync(
                request.OrganizerEmail,
                request.HatId,
                participant.Person.Email
            )
            .ConfigureAwait(false);

        // remove participant from hat
        await _giftExchangeProvider
            .DeleteParticipantAsync(request.OrganizerEmail, request.HatId, request.Email)
            .ConfigureAwait(false);

        if(hat.Status != HatStatus.InProgress) // any edits to participants should set hat status back to InProgress
            await _giftExchangeProvider
                .UpdateHatStatusAsync(request.OrganizerEmail, request.HatId, HatStatus.InProgress)
                .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.NoContent}, HttpStatusCode.NoContent);
    }

}
