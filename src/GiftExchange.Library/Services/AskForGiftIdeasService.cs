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

    private readonly ILogger<AskForGiftIdeasService> _logger;

    public AskForGiftIdeasService(
        GiftExchangeProvider giftExchangeProvider,
        IReplyThrottleProvider replyThrottleProvider,
        GiftIdeaEmailCompositionService composer,
        AskPageComposer pageComposer,
        AutomaticEmailSender sender,
        ILogger<AskForGiftIdeasService> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _replyThrottleProvider = replyThrottleProvider ?? throw new ArgumentNullException(nameof(replyThrottleProvider));
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
            : Page(_pageComposer.ComposeChoose(
                route.SenderPickedRecipient.Name, candidates, token, string.Empty));
    }

    private async Task<APIGatewayProxyResponse> SendAsksAsync(
        APIGatewayProxyRequest request,
        GiftIdeaRoute route,
        string token,
        ImmutableList<AskCandidate> candidates
    )
    {
        var subjectName = route.SenderPickedRecipient.Name;

        // Resolved against the database rather than against the list just rendered. The form came
        // back from a browser, and what a browser sends is whatever it was told to send.
        var targets = await _giftExchangeProvider
            .FindAskTargetsAsync(route.HatId, route.ParticipantId, ParseChoices(request))
            .ConfigureAwait(false);

        // Back to the same page rather than on to a results page with nothing on it. Ticking
        // nobody is a slip, and the useful response to a slip is the form again.
        if (targets.IsEmpty)
            return Page(_pageComposer.ComposeChoose(
                subjectName, candidates, token, "Choose at least one person to ask."));

        var attempts = ImmutableList.CreateBuilder<AskAttempt>();

        foreach (var target in targets)
            attempts.Add(await AskOneAsync(route, target).ConfigureAwait(false));

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

        return Page(_pageComposer.ComposeAskResults(subjectName, outcomes));
    }

    /// <summary>
    /// Asks one person, and says what became of it.
    /// </summary>
    /// <remarks>
    /// The throttle is claimed before anything else happens, so a refusal costs nothing — no email
    /// composed, and no token issued, which matters because a token issued for an ask that was
    /// never sent would be a live address nobody had been given.
    /// </remarks>
    private async Task<AskAttempt> AskOneAsync(GiftIdeaRoute route, AskTarget target)
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

        await SendAskAsync(route, target).ConfigureAwait(false);

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
    private Task SendAskAsync(GiftIdeaRoute route, AskTarget target) =>
        target.IsTheirPick switch
        {
            true => AskThemWhatTheyWouldLikeAsync(route, target),
            false => AskThemAboutThePickAsync(route, target)
        };

    /// <summary>
    /// Asks the person whose name the asker drew what they would like, for themselves.
    /// </summary>
    /// <remarks>
    /// A token of their own, issued alongside any they already hold rather than over them. Theirs
    /// cannot be reconstructed — only its hash was kept — so this is the only way to put a working
    /// SHARE GIFT IDEAS link into an email they did not originally receive.
    /// </remarks>
    private async Task AskThemWhatTheyWouldLikeAsync(GiftIdeaRoute route, AskTarget target)
    {
        var giftIdeasToken = await _giftExchangeProvider
            .IssueGiftIdeaTokenAsync(target.ParticipantId)
            .ConfigureAwait(false);

        await _sender.SendAsync(
                target.Person.Email,
                GiftIdeaEmailCompositionService.AskSubject(route.HatName),
                _composer.ComposeAsk(route.HatName, giftIdeasToken))
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
    private async Task AskThemAboutThePickAsync(GiftIdeaRoute route, AskTarget target)
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
                _composer.ComposeContributionAsk(
                    route.HatName, route.SenderPickedRecipient.Name, askToken))
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
    /// The participant ids ticked on the form.
    /// </summary>
    /// <remarks>
    /// Tolerant throughout: an unreadable body, an unparseable id or a duplicate produces a shorter
    /// list rather than an error. Nothing here decides anything on its own — every id survives only
    /// if the database agrees it belongs to this asker's exchange — so the useful thing to do with
    /// junk is to drop it and let the emptiness be reported as "choose somebody".
    /// </remarks>
    private static ImmutableList<Guid> ParseChoices(APIGatewayProxyRequest request)
    {
        var body = request.Body ?? string.Empty;

        if (request.IsBase64Encoded && body.Length > 0)
        {
            try
            {
                body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
            }
            catch (FormatException)
            {
                return [];
            }
        }

        var chosen = HttpUtility.ParseQueryString(body).GetValues(AskPageComposer.ChoiceField);

        if (chosen is null)
            return [];

        return
        [
            .. chosen
                .Select(value => Guid.TryParse(value, out var participantId) ? participantId : Guid.Empty)
                .Where(participantId => participantId != Guid.Empty)
                .Distinct()
        ];
    }

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
