namespace GiftExchange.Library.Services;

/// <summary>
/// Starts a new gift exchange from a finished one: the same people, the same eligibility rules,
/// and — by default — nobody drawing the person they drew last time.
/// </summary>
internal class CopyHatService : IApiGatewayHandler
{
    /// <summary>Worded exactly as it is for a hat created from scratch, so the advice matches.</summary>
    private const string NameTakenMessage =
        "A gift exchange with this name already exists. If this is the same gift exchange for a different year, try adding the year in the name to differentiate it from previous exchanges.";

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly ApiGatewayAdapter _adapter;

    private readonly DoNotAddService _doNotAddService;

    private readonly HatCreationLimiter _hatCreationLimiter;

    public CopyHatService(
        GiftExchangeProvider giftExchangeProvider,
        HatPreconditionValidator hatPreconditionValidator,
        ApiGatewayAdapter adapter,
        DoNotAddService doNotAddService,
        HatCreationLimiter hatCreationLimiter
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _doNotAddService = doNotAddService ?? throw new ArgumentNullException(nameof(doNotAddService));
        _hatCreationLimiter = hatCreationLimiter ?? throw new ArgumentNullException(nameof(hatCreationLimiter));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<CopyHatRequest, CopyHatResponse>(request, CopyHatAsync);

    internal async Task<Result<CopyHatResponse>> CopyHatAsync(CopyHatRequest request)
    {
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = new Dictionary<string, string>
                {
                    ["gift exchange name"] = request.NewHatName
                },
                // Only a revealed exchange can be copied. Before that the picks are still secret,
                // and leaving out last year's recipient would quietly disclose them.
                ValidHatStatuses = [HatStatus.Closed]
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<CopyHatResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode);

        var (nameTaken, _) = await _giftExchangeProvider
            .DoesHatAlreadyExistAsync(request.OrganizerEmail, request.NewHatName)
            .ConfigureAwait(false);

        if (nameTaken)
            return new Result<CopyHatResponse>(
                new InvalidOperationException(NameTakenMessage),
                HttpStatusCode.Conflict);

        // A copy spends the same allowances as an exchange created from scratch, and is
        // checked at the same point in the sequence: once everything that would have refused this
        // request on its own terms has passed.
        var limit = await _hatCreationLimiter
            .CheckAsync(request.OrganizerEmail)
            .ConfigureAwait(false);

        if (!limit.WithinLimit)
            return new Result<CopyHatResponse>(
                new InvalidOperationException(limit.RefusalMessage),
                limit.RefusalStatusCode);

        var sourceHat = hatPreconditionResult.Hat;

        // Measured against the source rather than what the copy turns out to hold. The refusals
        // read below can only make the copy smaller, so a source inside the limit cannot produce a
        // copy outside it, and working the exact figure out here would mean keeping a second copy
        // of the rule CopyHatAsync applies inside its own transaction.
        if (sourceHat.Participants.Count > ParticipantLimit.MaxParticipants)
            return new Result<CopyHatResponse>(
                new InvalidOperationException(ParticipantLimit.CopyRefusalMessage),
                HttpStatusCode.Conflict);

        var newHat = new HatDataModel
        {
            HatId = Guid.NewGuid(),
            HatName = request.NewHatName,
            Status = HatStatus.InProgress,
            AdditionalInformation = sourceHat.AdditionalInformation,
            PriceRange = sourceHat.PriceRange,
            OrganizerEmail = request.OrganizerEmail,
            OrganizerName = sourceHat.Organizer.Name
        };

        // Copying is the one path that adds a whole exchange's worth of people at once, and so the
        // one place a refusal would otherwise be silently reversed a year later. Checked against
        // the source hat's id, not the new one: the refusals were written against the exchange
        // somebody left, and the copy has no list of its own yet.
        //
        // Refusals drop people out of the copy rather than failing it. An organizer copying last
        // year's exchange has done nothing wrong, and refusing the whole thing would leave them
        // with no way forward but to work out by elimination which of their friends had opted out.
        var refused = await _doNotAddService
            .FindRefusedAsync(new DoNotAddCheckRequest
            {
                Emails = [.. sourceHat.Participants.Select(participant => participant.Person.Email)],
                OrganizerEmail = request.OrganizerEmail,
                HatId = request.HatId
            })
            .ConfigureAwait(false);

        var copied = await _giftExchangeProvider
            .CopyHatAsync(new CopyHatDataRequest
            {
                SourceHatId = request.HatId,
                NewHat = newHat,
                ExcludePreviousRecipients = request.ExcludePreviousRecipients,
                RefusedEmails = refused
            })
            .ConfigureAwait(false);

        if (!copied)
            return new Result<CopyHatResponse>(
                new InvalidOperationException(NameTakenMessage),
                HttpStatusCode.Conflict);

        // Counted from the participants actually left out rather than from the size of the refusal
        // set, because the organizer is carried over even if their own address is on a list, and
        // because an address can be on more than one of the three.
        var participantsOmitted = sourceHat.Participants
            .Count(participant => !participant.Person.Email.ContentEquals(sourceHat.Organizer.Email)
                                  && refused.Contains(participant.Person.Email.ToNormalizedEmail()));

        return new Result<CopyHatResponse>(
            new CopyHatResponse { HatId = newHat.HatId, ParticipantsOmitted = participantsOmitted },
            HttpStatusCode.Created);
    }
}
