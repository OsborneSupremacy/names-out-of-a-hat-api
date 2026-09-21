namespace GiftExchange.Library.Services;

internal class AssignRecipientsService : IApiGatewayHandler
{
    private readonly ILogger<AssignRecipientsService> _logger;

    private readonly ApiGatewayAdapter _adapter;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    /// <summary>
    /// Attempts for a draw with nothing on it but the organizer's exclusions. Unchanged from when
    /// that was the only kind of draw there was.
    /// </summary>
    private const int ShakeAttempts = 25;

    /// <summary>
    /// Attempts for the draw types that add a rule of their own. They fail more often for reasons
    /// that are luck rather than impossibility — a walk that boxed itself in, a chain that could
    /// not close — and each attempt costs a few thousand operations on a list that is almost always
    /// under thirty people, so trying an order of magnitude harder before declaring a configuration
    /// unsatisfiable is nearly free and much less likely to be wrong.
    /// </summary>
    private const int ConstrainedShakeAttempts = 250;

    private const string NonViableConfigurationMessage = "We've tried shaking the hat multiple times but we could not find a valid distribution (i.e. everyone is assigned to exactly one other participant). This can sometimes happen with certain configurations of participants and their eligible recipients. You can try shaking the hat again, but if the issue persists please review the list of participants and their eligible recipients to ensure that a valid distribution is possible.";

    /// <summary>
    /// What to say when a draw that had a rule on it could not be made. Names the rule, because it
    /// is the most likely thing standing in the way and the one thing the organizer can relax
    /// without editing anybody's exclusions.
    /// </summary>
    private static string ConstrainedNonViableConfigurationMessage(string drawType) =>
        $"We've tried shaking the hat multiple times but we could not find a valid distribution with the \"{DrawTypes.Describe(drawType)}\" rule applied on top of who each participant is allowed to draw. That combination may not be possible for this group. You can try shaking the hat again, choose \"Anything goes\" instead, or review the list of participants and their eligible recipients.";

    public AssignRecipientsService(
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
        _adapter.AdaptAsync<AssignRecipientsRequest, StatusCodeOnlyResponse>(request, AssignRecipientsAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> AssignRecipientsAsync(
        AssignRecipientsRequest request
        )
    {
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = [],
                ValidHatStatuses = [
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

        var isConstrained = DrawTypes.IsConstrained(request.DrawType);
        var attempts = isConstrained ? ConstrainedShakeAttempts : ShakeAttempts;

        var shakeResponse = HatShakerService.Shake(new ShakeHatRequest
        {
            Participants = hat.Participants,
            DrawType = request.DrawType,
            Attempts = attempts
        });

        if (!shakeResponse.Success)
        {
            _logger.LogWarning("Hat Id {HatId} for organizer {OrganizerEmail} could not be shaken successfully as a {DrawType} draw after {ShakeAttempts} attempts. This likely indicates a non-viable configuration of participants and eligible recipients.", request.HatId, request.OrganizerEmail, request.DrawType, attempts);

            var message = isConstrained
                ? ConstrainedNonViableConfigurationMessage(request.DrawType)
                : NonViableConfigurationMessage;

            return new Result<StatusCodeOnlyResponse>(new OperationCanceledException(message), HttpStatusCode.ServiceUnavailable);
        }

        var updateParticipantsTasks = new List<Task>();

        foreach (var participant in shakeResponse.Participants)
            updateParticipantsTasks.Add(_giftExchangeProvider
                .UpdateParticipantPickedRecipientAsync(
                    request.OrganizerEmail,
                    request.HatId,
                    participant.Person.Email,
                    participant.PickedRecipient.Email
                ));

        await Task.WhenAll(updateParticipantsTasks)
            .ConfigureAwait(false);

        await _giftExchangeProvider
            .UpdateHatStatusAsync(request.OrganizerEmail, request.HatId, HatStatus.NamesAssigned)
            .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.OK}, HttpStatusCode.OK);
    }
}
