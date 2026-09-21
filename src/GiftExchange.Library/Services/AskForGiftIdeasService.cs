using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// The Ask: one participant asking for gift ideas about the person whose name they drew, without
/// being named.
/// </summary>
/// <remarks>
/// Who they ask is up to them. Asking the person themselves is the obvious route and stays the
/// default, but it is not always the useful one — somebody who does not want to tip off their own
/// mother, however anonymous the email claims to be, can ask her husband and her daughter-in-law
/// instead, and get answers from people who will not spend the next month wondering. So the same
/// button now offers the whole exchange, and any number of them can be asked at once.
///
/// What that costs is a weaker kind of anonymity, and it cannot be engineered away. Being asked
/// what you would like reveals nothing: everybody is drawn by exactly one person, so the recipient
/// already knew somebody held their name. Being asked what somebody else would like reveals that
/// the asker drew that somebody — and the reader knows it was not them and not the subject, so in a
/// small exchange the remaining field is very short. The page says so before anybody chooses, which
/// is the only honest place to put it: the asker is the one person who knows whether the people
/// they have in mind will bother working it out.
///
/// Two endpoints for one action, and the split is the security design rather than an accident of
/// REST. The button lives in an email, so following it is a GET — and mail security scanners,
/// Microsoft Defender Safe Links among them, fetch links in delivered mail to check them. A GET
/// that sent the Ask would therefore fire on delivery for a large share of recipients: their
/// throttle window spent, and somebody mailed on behalf of a person who had not yet read the
/// invitation, let alone clicked anything.
///
/// So the GET only renders the list of people they could ask, which a scanner is welcome to fetch
/// as often as it likes, and the POST behind the button on that page does the work.
/// </remarks>
[UsedImplicitly]
internal class AskForGiftIdeasService : IApiGatewayHandler
{
    /// <summary>
    /// How long a participant has to wait before asking the same person again.
    ///
    /// A week, because the thing being asked for takes days to think about, and because the person
    /// on the receiving end cannot tell repeated asks from nagging — they do not know how many
    /// people are asking, only how often they are being asked. Short enough that somebody who
    /// genuinely got no answer can try again within the life of an exchange.
    ///
    /// Held per pair, so choosing five people costs five separate windows rather than one. See the
    /// remarks on <see cref="IReplyThrottleProvider.TryReserveAskSlotAsync"/>.
    /// </summary>
    private static readonly TimeSpan AskWindow = TimeSpan.FromDays(7);

    /// <summary>Statuses during which there is still somebody to ask.</summary>
    private static readonly ImmutableList<string> AskableStatuses =
        [HatStatus.NamesAssigned, HatStatus.InvitationsSent, HatStatus.CooledOff];

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly IReplyThrottleProvider _replyThrottleProvider;

    private readonly GiftIdeaEmailCompositionService _composer;

    private readonly AskPageComposer _pageComposer;

    private readonly AutomaticEmailSender _sender;

    private readonly AskQuestionPolicy _questionPolicy;

    private readonly IContentModerationService _contentModerationService;

    private readonly ILogger<AskForGiftIdeasService> _logger;

    public AskForGiftIdeasService(
        GiftExchangeProvider giftExchangeProvider,
        IReplyThrottleProvider replyThrottleProvider,
        GiftIdeaEmailCompositionService composer,
        AskPageComposer pageComposer,
        AutomaticEmailSender sender,
        AskQuestionPolicy questionPolicy,
        IContentModerationService contentModerationService,
        ILogger<AskForGiftIdeasService> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _replyThrottleProvider = replyThrottleProvider ?? throw new ArgumentNullException(nameof(replyThrottleProvider));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _pageComposer = pageComposer ?? throw new ArgumentNullException(nameof(pageComposer));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _questionPolicy = questionPolicy ?? throw new ArgumentNullException(nameof(questionPolicy));
        _contentModerationService = contentModerationService ?? throw new ArgumentNullException(nameof(contentModerationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    )
    {
        // Case preserved. The token is base64url, so "aB" and "Ab" are different tokens, and the
        // ordinary instinct to normalise an identifier from a URL would break every Ask.
        var token = request.PathParameters is not null
            && request.PathParameters.TryGetValue("token", out var pathToken)
                ? pathToken
                : string.Empty;

        var (found, route) = await _giftExchangeProvider
            .FindGiftIdeaRouteAsync(SecretToken.Hash(token))
            .ConfigureAwait(false);

        // Four dead ends, one page, and the sameness is the point rather than a shortcut. Telling
        // an unknown token apart from a finished exchange would let somebody holding a guessed one
        // learn whether it named a real participant, and the pair below say there is no pick —
        // which leaves nothing to ask about, of the pick or of anybody else.
        if (!found
            || !AskableStatuses.Contains(route.HatStatus)
            || route.SenderPickedRecipientParticipantId == Guid.Empty
            || string.IsNullOrWhiteSpace(route.SenderPickedRecipient.Email))
            return Page(AskPageComposer.ComposeUnavailable());

        var candidates = await _giftExchangeProvider
            .ListAskCandidatesAsync(route.HatId, route.ParticipantId)
            .ConfigureAwait(false);

        // An exchange of one. Nothing sends this state, but a page offering an empty list with a
        // send button is worse than saying the link is not available.
        if (candidates.IsEmpty)
            return Page(AskPageComposer.ComposeUnavailable());

        return request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
            ? await SendAsksAsync(request, route, token, candidates).ConfigureAwait(false)
            : Page(_pageComposer.ComposeChoose(new ComposeChooseRequest
            {
                SubjectName = route.SenderPickedRecipient.Name,
                Candidates = candidates,
                AskToken = token,
                Notice = string.Empty,
                Question = string.Empty,
                Chosen = []
            }));
    }

    private async Task<APIGatewayProxyResponse> SendAsksAsync(
        APIGatewayProxyRequest request,
        GiftIdeaRoute route,
        string token,
        ImmutableList<AskCandidate> candidates
    )
    {
        var subjectName = route.SenderPickedRecipient.Name;
        var submission = ReadSubmission(request);

        // Resolved against the database rather than against the list just rendered. The form came
        // back from a browser, and what a browser sends is whatever it was told to send.
        var targets = await _giftExchangeProvider
            .FindAskTargetsAsync(route.HatId, route.ParticipantId, submission.Chosen)
            .ConfigureAwait(false);

        // The form again, with whatever they typed and ticked still in it.
        APIGatewayProxyResponse Retry(string notice) =>
            Page(_pageComposer.ComposeChoose(new ComposeChooseRequest
            {
                SubjectName = subjectName,
                Candidates = candidates,
                AskToken = token,
                Notice = notice,
                Question = submission.Question,
                Chosen = [.. targets.Select(target => target.ParticipantId)]
            }));

        // Back to the same page rather than on to a results page with nothing on it. Ticking
        // nobody is a slip, and the useful response to a slip is the form again.
        if (targets.IsEmpty)
            return Retry("Choose at least one person to ask.");

        // Before anything else happens, including the release of held ideas and the claiming of
        // throttle slots. A question sent back to be fixed should cost the asker nothing, and a
        // week's wait on somebody they never actually asked would be a strange price for a typo.
        var questionOutcome = await CheckQuestionAsync(submission.Question, route).ConfigureAwait(false);

        if (questionOutcome != AskQuestionOutcome.Accepted)
            return Retry(AskPageComposer.ExplainRefusal(questionOutcome));

        // Before anybody is asked, because this is what the asker is owed already. Recorded even if
        // every ask below is then refused by the throttle: the throttle governs how often somebody
        // may be mailed, and what is written down here is that this participant asked.
        var released = await ReleaseHeldIdeasAsync(route).ConfigureAwait(false);

        var attempts = ImmutableList.CreateBuilder<AskAttempt>();

        foreach (var target in targets)
            attempts.Add(await AskOneAsync(route, target, submission.Question).ConfigureAwait(false));

        var outcomes = attempts.ToImmutable();

        // Only when something did not happen. Everything that went through is already on the page
        // in front of them, and a second copy by email of a round that worked is noise; a round
        // that fell short is worth having in writing, because the page is gone the moment they
        // close the tab.
        if (outcomes.Any(attempt => !attempt.Sent))
            await _sender.SendAsync(
                    route.Sender.Email,
                    GiftIdeaEmailCompositionService.AskPartiallySentSubject,
                    _composer.ComposeAskSummary(subjectName, outcomes))
                .ConfigureAwait(false);

        return Page(_pageComposer.ComposeAskResults(new ComposeAskResultsRequest
        {
            SubjectName = subjectName,
            Attempts = outcomes,
            ReleasedHeldIdeas = released
        }));
    }

    /// <summary>
    /// Writes down that this participant asked about their pick, and passes on anything the pick
    /// wrote and asked us to hold until somebody did.
    /// </summary>
    /// <remarks>
    /// The record outlives the round of asking, and has to. A participant may write held ideas long
    /// after they were asked for — asking the pick directly used to leave nothing behind but a
    /// throttle entry that expires within the week — and the share page consults this to decide
    /// whether anybody is waiting.
    ///
    /// The release is decided by comparing the submission against
    /// <see cref="GiftIdeaEnquiryEntity.ReleasedAt"/> rather than by whether the enquiry was new.
    /// Nothing here can tell whether an email went out, so a release that was dropped has to be
    /// repeatable, and one that arrived must not be sent twice. The comparison gives both: once per
    /// submission, and again if a later submission replaces it.
    ///
    /// The newest submission decides, and only then its flag. Somebody who held ideas back and later
    /// shared outright has already had the later version delivered, and the earlier held one is not
    /// what they want their giver reading now.
    ///
    /// The pick is never told any of this. Being told would be being told that whoever holds their
    /// name has been asking about them, which is the one thing asking other people instead of them was
    /// meant to avoid.
    /// </remarks>
    private async Task<bool> ReleaseHeldIdeasAsync(GiftIdeaRoute route)
    {
        var subjectParticipantId = route.SenderPickedRecipientParticipantId;

        var enquiry = await _giftExchangeProvider
            .RecordGiftIdeaEnquiryAsync(new RecordGiftIdeaEnquiryRequest
            {
                // The subject cannot be empty here: the handler has already shown the unavailable
                // page when this participant has no pick, so there is nothing to re-derive.
                AskerParticipantId = route.ParticipantId,
                SubjectParticipantId = subjectParticipantId
            })
            .ConfigureAwait(false);

        var latest = await _giftExchangeProvider
            .GetLatestGiftIdeaAsync(subjectParticipantId)
            .ConfigureAwait(false);

        if (!latest.HoldUntilAsked
            || string.IsNullOrEmpty(latest.Ideas)
            || latest.CreatedAt <= enquiry.ReleasedAt)
            return false;

        await _sender.SendAsync(
                route.Sender.Email,
                GiftIdeaEmailCompositionService.ForwardSubject(route.SenderPickedRecipient.Name),
                _composer.ComposeHeldForward(
                    route.SenderPickedRecipient.Name, route.HatName, latest.Ideas))
            .ConfigureAwait(false);

        // After the send, so that a message that never went out is tried again by the next ask.
        await _giftExchangeProvider
            .MarkGiftIdeaEnquiryReleasedAsync(new MarkGiftIdeaEnquiryReleasedRequest
            {
                AskerParticipantId = route.ParticipantId,
                SubjectParticipantId = subjectParticipantId,
                ReleasedAt = DateTimeOffset.UtcNow
            })
            .ConfigureAwait(false);

        _logger.LogInformation("Released a held gift ideas submission to somebody who asked.");

        return true;
    }

    /// <summary>
    /// Asks one person, and says what became of it.
    /// </summary>
    /// <remarks>
    /// The throttle is claimed before anything else happens, so a refusal costs nothing — no email
    /// composed, and no token issued, which matters because a token issued for an ask that was
    /// never sent would be a live address nobody had been given.
    /// </remarks>
    private async Task<AskAttempt> AskOneAsync(GiftIdeaRoute route, AskTarget target, string question)
    {
        var slot = await _replyThrottleProvider
            .TryReserveAskSlotAsync(new ReserveAskSlotRequest
            {
                AskerParticipantId = route.ParticipantId,
                TargetParticipantId = target.ParticipantId,
                Window = AskWindow
            })
            .ConfigureAwait(false);

        if (!slot.Reserved)
        {
            _logger.LogInformation("Suppressed an Ask inside the throttle window.");

            return Refused(target, slot.PreviouslyReservedAt);
        }

        await SendAskAsync(route, target, question).ConfigureAwait(false);

        return Sent(target);
    }

    /// <summary>
    /// Sends whichever of the two asks this person is due.
    /// </summary>
    /// <remarks>
    /// Both name nobody. Asking somebody what they would like gives nothing away at all; asking
    /// somebody what a third person would like gives away that the asker drew that third person,
    /// which is the trade the page has already put to them.
    /// </remarks>
    private Task SendAskAsync(GiftIdeaRoute route, AskTarget target, string question) =>
        target.IsTheirPick switch
        {
            true => AskThemWhatTheyWouldLikeAsync(route, target, question),
            false => AskThemAboutThePickAsync(route, target, question)
        };

    /// <summary>
    /// Asks the person whose name the asker drew what they would like, for themselves.
    /// </summary>
    /// <remarks>
    /// A token of their own, issued alongside any they already hold rather than over them. Theirs
    /// cannot be reconstructed — only its hash was kept — so this is the only way to put a working
    /// SHARE GIFT IDEAS link into an email they did not originally receive.
    /// </remarks>
    private async Task AskThemWhatTheyWouldLikeAsync(GiftIdeaRoute route, AskTarget target, string question)
    {
        var giftIdeasToken = await _giftExchangeProvider
            .IssueGiftIdeaTokenAsync(target.ParticipantId)
            .ConfigureAwait(false);

        await _sender.SendAsync(
                target.Person.Email,
                GiftIdeaEmailCompositionService.AskSubject(route.HatName),
                _composer.ComposeAsk(new ComposeAskRequest
                {
                    HatName = route.HatName,
                    GiftIdeasToken = giftIdeasToken,
                    Question = question
                }))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks somebody else in the exchange what they think the asker's pick would like.
    /// </summary>
    /// <remarks>
    /// The subject is written into the ask rather than followed back through the asker's pick
    /// later, so that the name in this email and the name the reply is filed under stay the same
    /// even if the organizer edits the draw afterwards.
    /// </remarks>
    private async Task AskThemAboutThePickAsync(GiftIdeaRoute route, AskTarget target, string question)
    {
        var askToken = await _giftExchangeProvider
            .IssueGiftIdeaAskAsync(
                route.ParticipantId,
                target.ParticipantId,
                route.SenderPickedRecipientParticipantId)
            .ConfigureAwait(false);

        await _sender.SendAsync(
                target.Person.Email,
                GiftIdeaEmailCompositionService.ContributionAskSubject(route.SenderPickedRecipient.Name),
                _composer.ComposeContributionAsk(new ComposeContributionAskRequest
                {
                    HatName = route.HatName,
                    SubjectName = route.SenderPickedRecipient.Name,
                    AskToken = askToken,
                    Question = question
                }))
            .ConfigureAwait(false);
    }

    /// <summary>An ask that went out.</summary>
    /// <remarks>
    /// The date is <see cref="DateTimeOffset.MinValue"/> because it means "no earlier ask stood in
    /// the way", which is a different fact from a date nobody recorded — and the callers reporting
    /// this never read the date off a sent attempt anyway.
    /// </remarks>
    private static AskAttempt Sent(AskTarget target) =>
        new()
        {
            Name = target.Person.Name,
            Sent = true,
            PreviouslyAskedAt = DateTimeOffset.MinValue
        };

    /// <summary>An ask the throttle refused, with the date it is refusing on behalf of.</summary>
    private static AskAttempt Refused(AskTarget target, DateTimeOffset previouslyAskedAt) =>
        new()
        {
            Name = target.Person.Name,
            Sent = false,
            PreviouslyAskedAt = previouslyAskedAt
        };

    /// <summary>
    /// Whether the question may go out with the asks.
    /// </summary>
    /// <remarks>
    /// Cheapest first, so a question refused on a rule this application can apply itself never
    /// reaches Comprehend — and no question at all never reaches it either.
    /// </remarks>
    private async Task<AskQuestionOutcome> CheckQuestionAsync(string question, GiftIdeaRoute route)
    {
        var policyOutcome = _questionPolicy.Check(question, route.Sender.Name);

        if (policyOutcome != AskQuestionOutcome.Accepted || string.IsNullOrEmpty(question))
            return policyOutcome;

        var verdict = await _contentModerationService
            .ModerateAsync(question, "question")
            .ConfigureAwait(false);

        // An outage is still a refusal, since nothing unchecked is sent. It is told apart so the
        // page says to try again rather than to reword something that may be perfectly fine.
        return verdict switch
        {
            ModerationVerdict.Clean => AskQuestionOutcome.Accepted,
            ModerationVerdict.Toxic => AskQuestionOutcome.RejectedInappropriateContent,
            _ => AskQuestionOutcome.RejectedModerationUnavailable
        };
    }

    /// <summary>
    /// The participant ids ticked on the form, and the question if one was written.
    /// </summary>
    /// <remarks>
    /// Tolerant throughout: an unreadable body, an unparseable id or a duplicate produces a shorter
    /// list rather than an error. Nothing here decides anything on its own — every id survives only
    /// if the database agrees it belongs to this asker's exchange — so the useful thing to do with
    /// junk is to drop it and let the emptiness be reported as "choose somebody".
    ///
    /// Line endings are normalised the way the share page's are, so that a question typed in one
    /// browser is counted and quoted the same as in any other.
    /// </remarks>
    private static AskSubmission ReadSubmission(APIGatewayProxyRequest request)
    {
        var fields = FormBody.Read(request);

        return new AskSubmission(
            [
                .. fields.All(AskPageComposer.ChoiceField)
                    .Select(value => Guid.TryParse(value, out var participantId) ? participantId : Guid.Empty)
                    .Where(participantId => participantId != Guid.Empty)
                    .Distinct()
            ],
            fields.First(AskPageComposer.QuestionField).Replace("\r\n", "\n").Replace('\r', '\n').Trim());
    }

    /// <summary>What the Ask form carried.</summary>
    private readonly record struct AskSubmission(ImmutableList<Guid> Chosen, string Question);

    /// <summary>
    /// Every outcome is a 200 carrying a page, including the ones that did nothing.
    /// </summary>
    /// <remarks>
    /// A status code would be read by the scanner that fetched this before any person did, and
    /// there is nobody for a 404 to inform. The reader is a human looking at a browser tab, so the
    /// page says what happened and the code stays out of it.
    /// </remarks>
    private static APIGatewayProxyResponse Page(string html) =>
        new()
        {
            StatusCode = (int)HttpStatusCode.OK,
            Body = html,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "text/html; charset=utf-8",
                // Nothing here is worth storing, and a cached Ask page shown after the fact would
                // report an outcome that is no longer true.
                ["Cache-Control"] = "no-store"
            }
        };
}
