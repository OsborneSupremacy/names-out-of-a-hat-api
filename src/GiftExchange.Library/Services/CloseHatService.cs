namespace GiftExchange.Library.Services;

internal class CloseHatService : IApiGatewayHandler
{
    private readonly ILogger<CloseHatService> _logger;

    private readonly ApiGatewayAdapter _adapter;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly CompletionEmailCompositionService _completionEmailCompositionService;

    private readonly IEmailQueue _emailQueue;

    public CloseHatService(
        ILogger<CloseHatService> logger,
        ApiGatewayAdapter adapter,
        GiftExchangeProvider giftExchangeProvider,
        HatPreconditionValidator hatPreconditionValidator,
        CompletionEmailCompositionService completionEmailCompositionService,
        IEmailQueue emailQueue
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
        _completionEmailCompositionService = completionEmailCompositionService ?? throw new ArgumentNullException(nameof(completionEmailCompositionService));
        _emailQueue = emailQueue ?? throw new ArgumentNullException(nameof(emailQueue));
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
                    HatStatus.CooledOff
                ]
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<StatusCodeOnlyResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode);

        await EnqueueCompletionEmailsAsync(hatPreconditionResult.Hat, request)
            .ConfigureAwait(false);

        await _giftExchangeProvider
            .UpdateHatStatusAsync(request.OrganizerEmail, request.HatId, HatStatus.Closed)
            .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.OK}, HttpStatusCode.OK);
    }

    /// <summary>
    /// Tells every participant that the exchange is over, and gives them the whole draw.
    /// </summary>
    /// <remarks>
    /// Before the status is written, not after, and that order is the answer to which failure is
    /// recoverable. A queue failure here leaves the hat at READY_TO_CLOSE, so the organizer sees an
    /// error and can press the button again. Closing first would leave a revealed exchange that can
    /// never be closed a second time, and therefore no way to send the mails at all — the
    /// preconditions above reject a second attempt.
    ///
    /// What that trades away is a duplicate: a partial failure re-sends to whoever already received
    /// one. Being told twice that the exchange has finished is a smaller harm than not being told.
    /// </remarks>
    private async Task EnqueueCompletionEmailsAsync(Hat hat, CloseHatRequest request)
    {
        _logger.LogInformation(
            "Queueing completion emails for {ParticipantCount} participants of hat {HatId}.",
            hat.Participants.Count,
            request.HatId);

        // As in EnqueueInvitationsService: the id the send is tagged with, so that what SES says
        // about this message lands on the right participant.
        var participantIds = await _giftExchangeProvider
            .GetParticipantIdsByEmailAsync(request.HatId)
            .ConfigureAwait(false);

        await Task.WhenAll(hat.Participants.Select(participant =>
                _emailQueue.EnqueueAsync(new GiftExchangeEmailRequest
                {
                    HatId = request.HatId,
                    OrganizerEmail = request.OrganizerEmail,
                    RecipientEmail = participant.Person.Email,
                    ParticipantId = participantIds.GetValueOrDefault(participant.Person.Email, Guid.Empty),
                    MessageType = EmailMessageType.Completion,
                    Subject = CompletionEmailCompositionService.GetSubject(hat),
                    HtmlBody = _completionEmailCompositionService.ComposeEmail(hat, participant.Person.Name),
                    SenderName = hat.Organizer.Name
                })))
            .ConfigureAwait(false);
    }
}
