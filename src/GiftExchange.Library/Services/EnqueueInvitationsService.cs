namespace GiftExchange.Library.Services;

[UsedImplicitly]
internal class EnqueueInvitationsService : IApiGatewayHandler
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly ApiGatewayAdapter _adapter;

    private readonly EmailCompositionService _emailCompositionService;

    private readonly IEmailQueue _emailQueue;

    private readonly ISchedulerService _schedulerService;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly OrganizerStandingChecker _organizerStandingChecker;

    public EnqueueInvitationsService(
        GiftExchangeProvider giftExchangeProvider,
        ApiGatewayAdapter adapter,
        HatPreconditionValidator hatPreconditionValidator,
        EmailCompositionService emailCompositionService,
        IEmailQueue emailQueue,
        ISchedulerService schedulerService,
        OrganizerStandingChecker organizerStandingChecker
        )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
        _emailCompositionService =
            emailCompositionService ?? throw new ArgumentNullException(nameof(emailCompositionService));
        _emailQueue = emailQueue ?? throw new ArgumentNullException(nameof(emailQueue));
        _schedulerService = schedulerService ?? throw new ArgumentNullException(nameof(schedulerService));
        _organizerStandingChecker = organizerStandingChecker ?? throw new ArgumentNullException(nameof(organizerStandingChecker));
    }

    // The address is read from the request context here rather than being carried on the request
    // body, so a caller cannot choose what gets recorded against their send.
    public Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context) =>
        _adapter.AdaptAsync<SendInvitationsRequest, StatusCodeOnlyResponse>(
            request,
            innerRequest => ExecuteAsync(innerRequest, request.GetSourceIpAddress()));

    internal async Task<Result<StatusCodeOnlyResponse>> ExecuteAsync(
        SendInvitationsRequest request,
        string sentFromIpAddress
    )
    {
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = [],
                ValidHatStatuses = [ HatStatus.NamesAssigned ]
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<StatusCodeOnlyResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode);

        // After the preconditions, so an organizer asking about somebody else's hat is told it does
        // not exist rather than anything about their own standing. Before any token is issued, so a
        // refused send leaves nothing behind that looks as though it went.
        var standing = await _organizerStandingChecker
            .CheckAsync(request.OrganizerEmail)
            .ConfigureAwait(false);

        if (!standing.MaySend)
            return new Result<StatusCodeOnlyResponse>(
                new InvalidOperationException(standing.RefusalMessage),
                standing.RefusalStatusCode);

        var hat = hatPreconditionResult.Hat;

        // Before anything is queued, because the token has to be inside the invitation. Issued here
        // rather than when a participant is added: this is the first moment there is an email going
        // to them to carry it, and a token nobody has been told is only a row.
        var giftIdeasTokens = await _giftExchangeProvider
            .IssueGiftIdeaTokensAsync(request.HatId)
            .ConfigureAwait(false);

        // Who each address belongs to within this hat, so the send can be tagged with it and what
        // SES reports afterwards can be matched to a participant. Read here rather than carried on
        // the domain record, which identifies people by name and address and not by id.
        // Alongside the gift ideas tokens, and for the same reason. The organizer is skipped by the
        // provider, so their own copy of the invitation carries no leave link — there is no leaving
        // an exchange you are running.
        var leaveTokens = await _giftExchangeProvider
            .IssueLeaveTokensAsync(request.HatId)
            .ConfigureAwait(false);

        var participantIds = await _giftExchangeProvider
            .GetParticipantIdsByEmailAsync(request.HatId)
            .ConfigureAwait(false);

        var enqueueTasks = new List<Task>();

        foreach(var participant in hat.Participants)
        {
            // An address with no token issued against it gets an invitation without the block, not
            // a broken one. Nothing should reach this, since the tokens were just written for every
            // participant in the hat.
            var giftIdeasToken = giftIdeasTokens.GetValueOrDefault(participant.Person.Email, string.Empty);

            // Empty for the organizer, deliberately, and empty is also what an address with no
            // token issued against it gets: an invitation without the leave sentence rather than
            // one carrying a link that goes nowhere.
            var leaveToken = leaveTokens.GetValueOrDefault(participant.Person.Email, string.Empty);

            var invitation = new GiftExchangeEmailRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                HtmlBody = _emailCompositionService.ComposeEmail(new ComposeInvitationRequest
                {
                    Hat = hat,
                    ParticipantName = participant.Person.Name,
                    PickedName = hat.DisplayNameIn(participant.PickedRecipient),
                    PickedEmoji = hat.EmojiFor(participant.PickedRecipient),
                    GiftIdeasToken = giftIdeasToken,
                    LeaveToken = leaveToken
                }),
                RecipientEmail = participant.Person.Email,
                // An address with no id against it still gets its invitation, exactly as one with
                // no token does. What it loses is the delivery status, which is the lesser harm --
                // and nothing should reach this, since these ids were read from the same hat.
                ParticipantId = participantIds.GetValueOrDefault(participant.Person.Email, Guid.Empty),
                MessageType = EmailMessageType.Invitation,
                Subject = EmailCompositionService.GetSubject(hat)
            };

            enqueueTasks.Add(_emailQueue.EnqueueAsync(invitation));
        }

        await Task.WhenAll(enqueueTasks)
            .ConfigureAwait(false);

        var invitationsQueuedAt = await _giftExchangeProvider
            .MarkInvitationsAsQueuedAsync(request.OrganizerEmail, request.HatId, sentFromIpAddress)
            .ConfigureAwait(false);

        await _schedulerService.CreateCooledOffScheduleAsync(request, invitationsQueuedAt)
            .ConfigureAwait(false);

        // Both schedules are counted from the one timestamp above rather than from the clock, so
        // that the cool-off and the delivery check cannot drift apart over the time these two calls
        // take. Neither throws: the invitations are already on the queue by this point, and a
        // failure here has to cost a schedule rather than a second round of mail.
        await _schedulerService.CreateUndeliverableInvitationsScheduleAsync(request, invitationsQueuedAt)
            .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.OK}, HttpStatusCode.OK);
    }
}
