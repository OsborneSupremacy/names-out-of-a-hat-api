namespace GiftExchange.Library.Services;

/// <summary>
/// Corrects the address one participant was invited at, and sends them what they missed.
/// </summary>
/// <remarks>
/// The reason this exists is the delivery column: an organizer can now see that an invitation
/// bounced, and until this endpoint the only remedy was to remove the participant and add them
/// back — which after the hat is shaken runs the full cleanup in
/// <c>GiftExchangeProvider.DeleteParticipantAsync</c> and quietly breaks the draw.
///
/// Distinct from <see cref="EditParticipantService"/>, which edits eligibility and resets the hat
/// to IN_PROGRESS. That reset is correct for a change made before the draw and ruinous after it.
///
/// The resend is automatic, because an address corrected after invitations went out is only ever
/// corrected because somebody did not receive theirs — a correction that left them still not
/// knowing would fix nothing. What is not automatic is the organizer's understanding of it: the
/// response says whether mail went out, so the interface can say so rather than leave them to
/// infer it from the hat's status.
/// </remarks>
[UsedImplicitly]
internal class EditParticipantAddressService : IApiGatewayHandler
{
    /// <summary>
    /// How long one participant's address is held after it is corrected.
    /// </summary>
    /// <remarks>
    /// Short on purpose. The point is not to make corrections rare — an organizer who typed two
    /// addresses wrongly should be able to fix both, and one who typos the correction itself should
    /// be able to try again without waiting until tomorrow. It is to stop the same participant
    /// being re-pointed in a loop, which is the only shape in which this endpoint is worth abusing.
    /// The friction an organizer actually feels is the confirmation in front of it.
    /// </remarks>
    private static readonly TimeSpan ChangeWindow = TimeSpan.FromMinutes(15);

    private readonly ILogger<EditParticipantAddressService> _logger;

    private readonly ApiGatewayAdapter _adapter;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly EmailCompositionService _emailCompositionService;

    private readonly CompletionEmailCompositionService _completionEmailCompositionService;

    private readonly IEmailQueue _emailQueue;

    private readonly IReplyThrottleProvider _throttleProvider;

    private readonly DoNotAddService _doNotAddService;

    public EditParticipantAddressService(
        ILogger<EditParticipantAddressService> logger,
        ApiGatewayAdapter adapter,
        HatPreconditionValidator hatPreconditionValidator,
        GiftExchangeProvider giftExchangeProvider,
        EmailCompositionService emailCompositionService,
        CompletionEmailCompositionService completionEmailCompositionService,
        IEmailQueue emailQueue,
        IReplyThrottleProvider throttleProvider,
        DoNotAddService doNotAddService
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _emailCompositionService = emailCompositionService ?? throw new ArgumentNullException(nameof(emailCompositionService));
        _completionEmailCompositionService = completionEmailCompositionService ?? throw new ArgumentNullException(nameof(completionEmailCompositionService));
        _emailQueue = emailQueue ?? throw new ArgumentNullException(nameof(emailQueue));
        _throttleProvider = throttleProvider ?? throw new ArgumentNullException(nameof(throttleProvider));
        _doNotAddService = doNotAddService ?? throw new ArgumentNullException(nameof(doNotAddService));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<EditParticipantAddressRequest, EditParticipantAddressResponse>(
            request,
            EditParticipantAddressAsync);

    internal async Task<Result<EditParticipantAddressResponse>> EditParticipantAddressAsync(
        EditParticipantAddressRequest request
    )
    {
        // Every status is valid here, which is unusual and deliberate: a wrong address is worth
        // fixing whenever it is noticed. The validator is still what establishes that this hat
        // exists and belongs to the caller.
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = [],
                ValidHatStatuses = HatStatuses.All
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<EditParticipantAddressResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode);

        var hat = hatPreconditionResult.Hat;

        var participant = hat.Participants
            .SingleOrDefault(candidate => candidate.Person.Email.ContentEquals(request.CurrentEmail));

        if (participant is null)
            return new Result<EditParticipantAddressResponse>(
                new KeyNotFoundException($"Participant with email `{request.CurrentEmail}` not found"),
                HttpStatusCode.NotFound);

        // Checked here as it is on an ordinary add, because this endpoint is otherwise a way around
        // that one: re-pointing an existing row at a refused address would put an invitation in the
        // inbox of somebody who had asked not to receive them, without ever calling the path that
        // looks. Ahead of the throttle reservation, so a refused correction spends no slot.
        var refused = await _doNotAddService
            .IsRefusedAsync(request.NewEmail, request.OrganizerEmail, request.HatId)
            .ConfigureAwait(false);

        if (refused)
            return new Result<EditParticipantAddressResponse>(
                new InvalidOperationException(DoNotAddService.RefusalMessage),
                HttpStatusCode.Forbidden);

        // Decided from the status as it stands, before anything is written. Nothing below changes
        // the hat's status, so reading it first is only about keeping the decision in one place.
        var messageType = MessageTypeFor(hat.Status);

        // Reserved before the address is written rather than after, so a refused change leaves
        // nothing half done. Only when something would actually be sent: correcting an address
        // before invitations go out mails nobody and needs no limit.
        if (messageType != EmailMessageType.Unspecified)
        {
            var participantIds = await _giftExchangeProvider
                .GetParticipantIdsByEmailAsync(request.HatId)
                .ConfigureAwait(false);

            var participantId = participantIds.GetValueOrDefault(request.CurrentEmail, Guid.Empty);

            var slot = await _throttleProvider
                .TryReserveAddressChangeSlotAsync(new ReserveAddressChangeSlotRequest
                {
                    ParticipantId = participantId,
                    Window = ChangeWindow
                })
                .ConfigureAwait(false);

            if (!slot.Reserved)
                return new Result<EditParticipantAddressResponse>(
                    new InvalidOperationException(
                        TooSoonMessage(participant.Person.Name, slot.PreviouslyReservedAt)),
                    HttpStatusCode.TooManyRequests);
        }

        var change = await _giftExchangeProvider
            .UpdateParticipantAddressAsync(new UpdateParticipantAddressRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                CurrentEmail = request.CurrentEmail,
                NewEmail = request.NewEmail
            })
            .ConfigureAwait(false);

        if (change.Outcome != AddressChangeOutcome.Changed)
            return Failure(change, request.NewEmail);

        _logger.LogInformation(
            "Participant {ParticipantId} in hat {HatId} was moved to a new address.",
            change.ParticipantId,
            request.HatId);

        // Before anything is resent, and whether or not anything is. The gift ideas links already
        // mailed went to the wrong inbox, and a link is all it takes to write ideas in this
        // participant's name.
        await _giftExchangeProvider
            .RevokeGiftIdeaLinksAsync(change.ParticipantId)
            .ConfigureAwait(false);

        if (messageType == EmailMessageType.Unspecified)
            return Success(resent: false, EmailMessageType.Unspecified);

        await ResendAsync(hat, participant, change, request, messageType)
            .ConfigureAwait(false);

        return Success(resent: true, messageType);
    }

    /// <summary>
    /// Which message a correction owes this participant, given where the exchange has got to.
    /// </summary>
    /// <remarks>
    /// A revealed exchange resends the announcement rather than the invitation. The invitation is
    /// no longer true by then — it asks the reader to keep a secret that everybody has been told —
    /// and the message somebody at a broken address actually missed is the one saying it is over.
    /// </remarks>
    private static string MessageTypeFor(string hatStatus)
    {
        if (hatStatus == HatStatus.InvitationsSent || hatStatus == HatStatus.CooledOff)
            return EmailMessageType.Invitation;

        if (hatStatus == HatStatus.Closed)
            return EmailMessageType.Completion;

        // Nothing has been sent to anybody yet, so there is nothing to resend and the correction is
        // only a correction.
        return EmailMessageType.Unspecified;
    }

    private async Task ResendAsync(
        Hat hat,
        Participant participant,
        UpdateParticipantAddressResponse change,
        EditParticipantAddressRequest request,
        string messageType
    )
    {
        string body;

        if (messageType == EmailMessageType.Completion)
        {
            body = _completionEmailCompositionService.ComposeEmail(hat, change.Name);
        }
        else
        {
            // A fresh one, because the ones the old address was sent were revoked before this
            // was called.
            var giftIdeasToken = await _giftExchangeProvider
                .IssueGiftIdeaTokenAsync(change.ParticipantId)
                .ConfigureAwait(false);

            // Replaced rather than revoked up front, because a new one is only wanted when an
            // invitation is resent. The old invitation went to an address that was wrong, and a
            // leave link in it removes this participant from the exchange — so whoever is reading
            // that inbox must not keep a working one.
            //
            // Not for the organizer. They are a participant of their own exchange and can have
            // their address corrected like anybody else, but there is no leaving an exchange you
            // are running, so their resent invitation carries no leave sentence.
            var leaveToken = participant.Person.Email.ContentEquals(hat.Organizer.Email)
                ? string.Empty
                : await _giftExchangeProvider
                    .IssueLeaveTokenAsync(change.ParticipantId)
                    .ConfigureAwait(false);

            body = _emailCompositionService.ComposeEmail(new ComposeInvitationRequest
            {
                Hat = hat,
                ParticipantName = change.Name,
                PickedName = participant.PickedRecipient,
                PickedEmoji = hat.EmojiFor(participant.PickedRecipient),
                GiftIdeasToken = giftIdeasToken,
                LeaveToken = leaveToken
            });
        }

        await _emailQueue.EnqueueAsync(new GiftExchangeEmailRequest
        {
            HatId = request.HatId,
            OrganizerEmail = request.OrganizerEmail,
            RecipientEmail = request.NewEmail,
            ParticipantId = change.ParticipantId,
            MessageType = messageType,
            Subject = messageType == EmailMessageType.Completion
                ? CompletionEmailCompositionService.GetSubject(hat)
                : EmailCompositionService.GetSubject(hat),
            HtmlBody = body
        }).ConfigureAwait(false);

        _logger.LogInformation(
            "Queued a {MessageType} email to the corrected address for participant {ParticipantId}.",
            messageType,
            change.ParticipantId);
    }

    private static Result<EditParticipantAddressResponse> Success(bool resent, string messageType) =>
        new(
            new EditParticipantAddressResponse
            {
                EmailResent = resent,
                MessageType = resent ? messageType : string.Empty
            },
            HttpStatusCode.OK);

    private static Result<EditParticipantAddressResponse> Failure(
        UpdateParticipantAddressResponse change,
        string newEmail
    ) =>
        change.Outcome switch
        {
            AddressChangeOutcome.ParticipantNotFound => new Result<EditParticipantAddressResponse>(
                new KeyNotFoundException("That participant is no longer in this gift exchange."),
                HttpStatusCode.NotFound),

            AddressChangeOutcome.AddressAlreadyInExchange => new Result<EditParticipantAddressResponse>(
                new InvalidOperationException(
                    $"Somebody else in this gift exchange is already using {newEmail}. Participants must have unique email addresses."),
                HttpStatusCode.Conflict),

            AddressChangeOutcome.NameAlreadyInExchange => new Result<EditParticipantAddressResponse>(
                new InvalidOperationException(
                    $"{newEmail} already belongs to somebody recorded under the name {change.Name}, and this gift exchange already has a participant with that name. Participants must have unique names."),
                HttpStatusCode.Conflict),

            _ => new Result<EditParticipantAddressResponse>(
                new InvalidOperationException("The address could not be changed."),
                HttpStatusCode.InternalServerError)
        };

    /// <summary>
    /// Names when, rather than only refusing, for the reason the Ask throttle does: an organizer
    /// told to wait needs to know what they are waiting for.
    /// </summary>
    private static string TooSoonMessage(string participantName, DateTimeOffset previouslyChangedAt) =>
        previouslyChangedAt == DateTimeOffset.MinValue
            ? $"{participantName}'s address was changed very recently. Please wait a few minutes before changing it again."
            : $"{participantName}'s address was already changed at {previouslyChangedAt:HH:mm} UTC. Please wait a few minutes before changing it again.";
}
