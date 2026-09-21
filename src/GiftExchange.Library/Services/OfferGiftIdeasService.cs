namespace GiftExchange.Library.Services;

/// <summary>
/// Offering gift ideas about another participant without being asked.
/// </summary>
/// <remarks>
/// The third way text reaches somebody in this application, and the first that nobody requested.
/// <see cref="ShareGiftIdeasService"/> carries what a participant says about themselves to whoever
/// drew them, and what a helper writes because somebody asked them to. This carries what somebody
/// writes because they happen to know the answer.
///
/// A service of its own rather than a third branch of that one. It tells its two paths apart by
/// which table the token resolved in, and this is neither of them: the token here is the
/// participant's own, the same one the SHARE GIFT IDEAS button carries, and the subject is chosen on
/// the page rather than settled by the link. A third arm keyed on <c>IsContribution</c> would mean a
/// flag that no longer says what its name says.
///
/// Two endpoints for one action, for the reason <see cref="AskForGiftIdeasService"/> gives: a
/// button in an email is followed with a GET, and mail scanners follow those on delivery. The GET
/// renders the form; the POST behind its button stores and sends.
///
/// Only the participant's own token opens this. An ask token is refused, deliberately: it
/// authorises answering the one ask it was issued for, not starting a conversation about anybody
/// else. Nothing new is issued here either, which means
/// <see cref="GiftExchangeProvider.RevokeGiftIdeaLinksAsync"/> already revokes this page along with
/// the rest when an organizer corrects an address.
///
/// <b>The confirmation page never says whether anything was sent.</b> See
/// <see cref="ForwardAsync"/> and <see cref="OfferIdeasPageComposer.ComposeShared"/>.
/// </remarks>
[UsedImplicitly]
internal class OfferGiftIdeasService : IApiGatewayHandler
{
    /// <summary>Statuses during which there is still somebody to share ideas with.</summary>
    private static readonly ImmutableList<string> AcceptingStatuses =
        [HatStatus.NamesAssigned, HatStatus.InvitationsSent, HatStatus.CooledOff];

    /// <summary>
    /// How long one participant must wait before offering ideas about the same person again.
    /// </summary>
    /// <remarks>
    /// A week, matching the Ask. This is the only limit on a message nobody asked for, so it is
    /// doing more work here than it does there: it caps what any one inbox receives from any one
    /// sender, which is the number that matters.
    /// </remarks>
    private static readonly TimeSpan OfferWindow = TimeSpan.FromDays(7);

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly IReplyThrottleProvider _replyThrottleProvider;

    private readonly GiftIdeaContentPolicy _contentPolicy;

    private readonly IContentModerationService _contentModerationService;

    private readonly GiftIdeaEmailCompositionService _composer;

    private readonly OfferIdeasPageComposer _pageComposer;

    private readonly AutomaticEmailSender _sender;

    private readonly ILogger<OfferGiftIdeasService> _logger;

    public OfferGiftIdeasService(
        GiftExchangeProvider giftExchangeProvider,
        IReplyThrottleProvider replyThrottleProvider,
        GiftIdeaContentPolicy contentPolicy,
        IContentModerationService contentModerationService,
        GiftIdeaEmailCompositionService composer,
        OfferIdeasPageComposer pageComposer,
        AutomaticEmailSender sender,
        ILogger<OfferGiftIdeasService> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _replyThrottleProvider = replyThrottleProvider ?? throw new ArgumentNullException(nameof(replyThrottleProvider));
        _contentPolicy = contentPolicy ?? throw new ArgumentNullException(nameof(contentPolicy));
        _contentModerationService = contentModerationService ?? throw new ArgumentNullException(nameof(contentModerationService));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _pageComposer = pageComposer ?? throw new ArgumentNullException(nameof(pageComposer));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    )
    {
        // Case preserved, as everywhere else: the token is base64url, so "aB" and "Ab" differ.
        var token = request.PathParameters is not null
            && request.PathParameters.TryGetValue("token", out var pathToken)
                ? pathToken
                : string.Empty;

        // The participant's own token and nothing else. No fall through to the contribution
        // lookup, for the reason this class's remarks give.
        var (found, route) = await _giftExchangeProvider
            .FindGiftIdeaRouteAsync(SecretToken.Hash(token))
            .ConfigureAwait(false);

        // One page for all of them, so that a guessed token cannot be told apart from a finished
        // exchange, and neither from an ask token used on the wrong page.
        if (!found || !AcceptingStatuses.Contains(route.HatStatus))
            return Page(ShareIdeasPageComposer.ComposeUnavailable());

        var candidates = await _giftExchangeProvider
            .ListOfferCandidatesAsync(route.HatId, route.ParticipantId)
            .ConfigureAwait(false);

        // An exchange of two, where the only other person is the reader's own pick. There is
        // nobody to write about, and a form with no names in it would be a puzzle rather than a
        // page.
        if (candidates.IsEmpty)
            return Page(ShareIdeasPageComposer.ComposeUnavailable());

        return request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
            ? await OfferAsync(route, token, candidates, ParseSubmission(request)).ConfigureAwait(false)
            : Page(_pageComposer.ComposeForm(new ComposeOfferIdeasFormRequest
            {
                Token = token,
                Candidates = candidates,
                // Nothing ticked on the way in. There is no ordinary choice to offer first.
                ChosenSubjectId = Guid.Empty,
                Ideas = string.Empty,
                Notice = string.Empty
            }));
    }

    /// <summary>
    /// Checks the submission, and either passes it on or hands the form back with the reason.
    /// </summary>
    /// <remarks>
    /// The order is deliberate and is not the Ask's. That one claims its throttle slot first,
    /// reasoning that a refusal costs nothing — true there, where nobody has written anything yet.
    /// Here the participant has already typed a paragraph, and spending their week on a submission
    /// that was then refused for a shortened link would be indefensible. So: resolve, then check,
    /// then claim, then store, then send.
    /// </remarks>
    private async Task<APIGatewayProxyResponse> OfferAsync(
        GiftIdeaRoute route,
        string token,
        ImmutableList<OfferCandidate> candidates,
        OfferedIdeasSubmission submission
    )
    {
        // Re-resolved against the database rather than trusted from the page, for the reason
        // FindOfferTargetAsync gives: this form was rendered by us and edited by them.
        var (found, target) = submission.SubjectParticipantId == Guid.Empty
            ? (false, OfferTargets.Empty)
            : await _giftExchangeProvider
                .FindOfferTargetAsync(new FindOfferTargetRequest
                {
                    HatId = route.HatId,
                    SharerParticipantId = route.ParticipantId,
                    SubjectParticipantId = submission.SubjectParticipantId
                })
                .ConfigureAwait(false);

        // One notice for "you picked nobody" and for "you picked somebody you may not pick". The
        // second is only reachable by editing the form, and somebody who has done that is not owed
        // an explanation of which name they were not allowed to use.
        if (!found)
            return Form(token, candidates, Guid.Empty, submission.Ideas, "Choose who these ideas are about.");

        var outcome = await CheckAsync(submission.Ideas, route).ConfigureAwait(false);

        if (outcome != GiftIdeaSubmissionOutcome.Shared)
        {
            _logger.LogInformation("Refused an offer of gift ideas: {Outcome}", outcome);

            return Form(
                token,
                candidates,
                target.SubjectParticipantId,
                submission.Ideas,
                ShareIdeasPageComposer.ExplainRefusal(outcome));
        }

        var slot = await _replyThrottleProvider
            .TryReserveOfferSlotAsync(new ReserveOfferSlotRequest
            {
                SharerParticipantId = route.ParticipantId,
                SubjectParticipantId = target.SubjectParticipantId,
                Window = OfferWindow
            })
            .ConfigureAwait(false);

        // Nothing is stored either. An offer that was not passed on is not a record of anything,
        // and keeping it would make the next week's submission look like a repeat.
        if (!slot.Reserved)
            return Page(_pageComposer.ComposeAlreadyShared(target.SubjectName, slot.PreviouslyReservedAt));

        // Stored before anything is sent. If sending fails after this the text still exists; the
        // reverse would lose what somebody wrote.
        await _giftExchangeProvider
            .AddOfferedGiftIdeaAsync(new AddOfferedGiftIdeaRequest
            {
                AuthorParticipantId = route.ParticipantId,
                SubjectParticipantId = target.SubjectParticipantId,
                Ideas = submission.Ideas
            })
            .ConfigureAwait(false);

        await ForwardAsync(route, target, submission.Ideas).ConfigureAwait(false);

        return Page(_pageComposer.ComposeShared(new ComposeOfferedIdeasRequest
        {
            SubjectName = target.SubjectName,
            Ideas = submission.Ideas
        }));
    }

    /// <summary>
    /// Sends the offer to whoever holds the subject's name, when there is somebody and it is not
    /// the sender.
    /// </summary>
    /// <remarks>
    /// Both silent outcomes return without touching the response, which is the whole point:
    /// <see cref="OfferAsync"/> renders the same page whichever of the three happened, so nobody can
    /// learn the draw by offering ideas about each participant in turn and watching what changes.
    ///
    /// The second check is unreachable today and is kept anyway. The candidate filter and the
    /// giver lookup both read <see cref="ParticipantEntity.PickedRecipientParticipantId"/>, so a
    /// subject that survived the first cannot resolve to the sender as its giver. They are separate
    /// queries that could be changed apart, though, and what this prevents — an offer coming back
    /// to its own author, carrying the name of the person they drew — is worth one comparison.
    ///
    /// Neither log line names anybody. A log that said who had no giver would be a record of the
    /// draw.
    /// </remarks>
    private Task ForwardAsync(GiftIdeaRoute route, OfferTarget target, string ideas)
    {
        if (string.IsNullOrWhiteSpace(target.Giver.Email))
        {
            _logger.LogInformation("Stored an offer of gift ideas with nobody yet to forward it to.");
            return Task.CompletedTask;
        }

        if (target.GiverParticipantId == route.ParticipantId)
        {
            _logger.LogInformation("Stored an offer of gift ideas that would have returned to its sender.");
            return Task.CompletedTask;
        }

        return _sender.SendAsync(
            target.Giver.Email,
            GiftIdeaEmailCompositionService.ContributionForwardSubject(route.DisplayNameOf(route.Sender), target.SubjectName),
            _composer.ComposeOfferedIdeasForward(route.DisplayNameOf(route.Sender), target.SubjectName, route.HatName, ideas));
    }

    /// <summary>
    /// Whether there is anything about this submission that stops it being passed on.
    /// </summary>
    /// <remarks>
    /// The same two checks in the same order <see cref="ShareGiftIdeasService"/> runs, cheapest
    /// first, so text refused on a rule this application can apply itself never reaches Comprehend.
    ///
    /// The name looked for is the sender's own pick, not the subject. The check is about what the
    /// sender must not leak, and the two cannot collide: the subject is somebody the sender did not
    /// draw, which is exactly who this page offers.
    /// </remarks>
    private async Task<GiftIdeaSubmissionOutcome> CheckAsync(string ideas, GiftIdeaRoute route)
    {
        var policyOutcome = _contentPolicy.Check(ideas, route.SenderPickedRecipient.Name);

        if (policyOutcome != GiftIdeaSubmissionOutcome.Shared)
            return policyOutcome;

        var verdict = await _contentModerationService
            .ModerateAsync(ideas, "gift ideas")
            .ConfigureAwait(false);

        // An outage is still a refusal, since nothing unchecked is forwarded. It is told apart so
        // the page says to try again rather than to reword something that may be perfectly fine.
        return verdict switch
        {
            ModerationVerdict.Clean => GiftIdeaSubmissionOutcome.Shared,
            ModerationVerdict.Toxic => GiftIdeaSubmissionOutcome.RejectedInappropriateContent,
            _ => GiftIdeaSubmissionOutcome.RejectedModerationUnavailable
        };
    }

    private APIGatewayProxyResponse Form(
        string token,
        ImmutableList<OfferCandidate> candidates,
        Guid chosenSubjectId,
        string ideas,
        string notice
    ) =>
        Page(_pageComposer.ComposeForm(new ComposeOfferIdeasFormRequest
        {
            Token = token,
            Candidates = candidates,
            ChosenSubjectId = chosenSubjectId,
            Ideas = ideas,
            Notice = notice
        }));

    /// <summary>
    /// What the form posted: who the ideas are about, and the text, trimmed and with line endings
    /// made consistent.
    /// </summary>
    /// <remarks>
    /// An unparseable subject is the all-zero id, which matches nobody and is answered the same way
    /// as choosing nobody at all. Nothing here decides whether the id is one this participant was
    /// allowed to choose — that is the database's answer, not the parser's.
    /// </remarks>
    private static OfferedIdeasSubmission ParseSubmission(APIGatewayProxyRequest request)
    {
        var fields = FormBody.Read(request);

        return new OfferedIdeasSubmission(
            Guid.TryParse(fields.First(OfferIdeasPageComposer.SubjectField), out var subjectId)
                ? subjectId
                : Guid.Empty,
            fields.First(ShareIdeasPageComposer.IdeasField).Replace("\r\n", "\n").Replace('\r', '\n').Trim());
    }

    /// <summary>What the two fields carry, before either has been checked.</summary>
    /// <remarks>
    /// Kept inside this class rather than put in Messaging, for the reason
    /// <see cref="ShareGiftIdeasService"/> gives about its own: it never crosses a boundary.
    /// </remarks>
    private readonly record struct OfferedIdeasSubmission(Guid SubjectParticipantId, string Ideas);

    /// <summary>
    /// Every outcome is a 200 carrying a page, for the reason <see cref="AskForGiftIdeasService"/>
    /// gives: the reader is a person looking at a browser tab, not something that reads status codes.
    /// </summary>
    private static APIGatewayProxyResponse Page(string html) =>
        new()
        {
            StatusCode = (int)HttpStatusCode.OK,
            Body = html,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "text/html; charset=utf-8",
                ["Cache-Control"] = "no-store"
            }
        };
}
