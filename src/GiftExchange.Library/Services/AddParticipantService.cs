namespace GiftExchange.Library.Services;

internal class AddParticipantService : IApiGatewayHandler
{
    private readonly ILogger<AddParticipantService> _logger;

    private readonly ApiGatewayAdapter _adapter;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly DoNotAddService _doNotAddService;

    public AddParticipantService(
        ILogger<AddParticipantService> logger,
        ApiGatewayAdapter adapter,
        HatPreconditionValidator hatPreconditionValidator,
        GiftExchangeProvider giftExchangeProvider,
        DoNotAddService doNotAddService
        )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _doNotAddService = doNotAddService ?? throw new ArgumentNullException(nameof(doNotAddService));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
        ) =>
        _adapter
            .AdaptAsync<AddParticipantRequest, StatusCodeOnlyResponse>(request, AddParticipantAsync);

    private async Task<Result<StatusCodeOnlyResponse>> AddParticipantAsync(AddParticipantRequest request)
    {
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId =  request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = new Dictionary<string, string>
                {
                    { "participant name", request.Name }
                },
                ValidHatStatuses = [ HatStatus.InProgress, HatStatus.ReadyForAssignment, HatStatus.NamesAssigned]
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<StatusCodeOnlyResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode
            );

        var hat = hatPreconditionResult.Hat;

        var existingParticipants = await _giftExchangeProvider
            .GetParticipantsAsync(request.OrganizerEmail, request.HatId)
            .ConfigureAwait(false);

        // The same person may not be in one exchange twice, and the address is what says who a
        // person is. Names are another matter: a person is one row shared by every exchange they
        // are in, so two people called Sam is an ordinary thing for an exchange to hold, and
        // anything naming one of them to somebody else adds the address — see ParticipantNaming.
        if (existingParticipants.Any(p => p.Person.Email.ContentEquals(request.Email)))
            return new Result<StatusCodeOnlyResponse>(new InvalidOperationException("Somebody with that email address is already in this gift exchange."), HttpStatusCode.Conflict);

        // After the duplicate check and before the write. Somebody who has refused this exchange, or
        // this organizer, or all of them, is not added back by an organizer typing their address in
        // again — which is the ordinary next thing to happen, since the organizer is told somebody
        // left and asked to draw names afresh.
        //
        // Forbidden rather than Conflict. A conflict says the request collided with something and
        // could be retried differently; this one cannot be retried at all with this address.
        var refused = await _doNotAddService
            .IsRefusedAsync(request.Email, request.OrganizerEmail, request.HatId)
            .ConfigureAwait(false);

        if (refused)
            return new Result<StatusCodeOnlyResponse>(
                new InvalidOperationException(DoNotAddService.RefusalMessage),
                HttpStatusCode.Forbidden);

        // Last of the refusals, after every one that is about this request in particular. A full
        // hat is a fact about the exchange rather than anything wrong with the person being added,
        // so an address that is also a duplicate or a refusal is told the more specific thing.
        //
        // Conflict rather than TooManyRequests, which is what the daily creation limit answers
        // with: nothing here is rate limited, and waiting changes nothing. The request has
        // collided with the state of the exchange, and removing somebody is what resolves it.
        //
        // Two adds arriving together can both read the same count and both be allowed, exactly as
        // HatCreationLimiter documents for its own check. Left alone for the same reason, and with
        // more room to be wrong in: the transaction ceiling this number is drawn from sits at 54,
        // so an exchange that overshoots by a couple can still be copied and deleted.
        if (existingParticipants.Count >= ParticipantLimit.MaxParticipants)
            return new Result<StatusCodeOnlyResponse>(
                new InvalidOperationException(ParticipantLimit.RefusalMessage),
                HttpStatusCode.Conflict);

        await _giftExchangeProvider
            .CreateParticipantAsync(
                new AddParticipantRequest
                {
                    OrganizerEmail = request.OrganizerEmail,
                    HatId = request.HatId,
                    Name = request.Name,
                    Email = request.Email
                }, existingParticipants)
            .ConfigureAwait(false);

        // make new participant eligible for all existing participants
        var tasks = existingParticipants
            .Select(participant =>
                _giftExchangeProvider
                    .AddParticipantEligibleRecipientAsync(
                        request.OrganizerEmail,
                        request.HatId,
                        participant.Person.Email,
                        request.Email
                    ))
            .ToList();

        if (hat.Status != HatStatus.InProgress)
            tasks.Add(
                _giftExchangeProvider
                    .UpdateHatStatusAsync(request.OrganizerEmail, request.HatId, HatStatus.InProgress)
                );

        await Task
            .WhenAll(tasks)
            .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(
            new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.Created},
            HttpStatusCode.Created
        );
    }
}
