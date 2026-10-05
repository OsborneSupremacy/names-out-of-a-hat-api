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
///
/// The work itself is <see cref="GiftIdeaAsking"/>'s, which the participant's signed-in page shares;
/// this class is the token and the HTML around it.
/// </remarks>
[UsedImplicitly]
internal class AskForGiftIdeasService : IApiGatewayHandler
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly GiftIdeaAsking _asking;

    private readonly AskPageComposer _pageComposer;

    public AskForGiftIdeasService(
        GiftExchangeProvider giftExchangeProvider,
        GiftIdeaAsking asking,
        AskPageComposer pageComposer
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _asking = asking ?? throw new ArgumentNullException(nameof(asking));
        _pageComposer = pageComposer ?? throw new ArgumentNullException(nameof(pageComposer));
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
        if (!found || !GiftIdeaAsking.CanAsk(route))
            return Page(AskPageComposer.ComposeUnavailable());

        var candidates = await _giftExchangeProvider
            .ListAskCandidatesAsync(route.HatId, route.ParticipantId)
            .ConfigureAwait(false);

        // An exchange of one. Nothing sends this state, but a page offering an empty list with a
        // send button is worse than saying the link is not available.
        if (candidates.IsEmpty)
            return Page(AskPageComposer.ComposeUnavailable());

        if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            // Whatever becomes of the asks: somebody pressed the button, and that is what the
            // invitation reminder wants to know.
            await _giftExchangeProvider.MarkGiftIdeaTokenUsedAsync(SecretToken.Hash(token)).ConfigureAwait(false);

            return await SendAsksAsync(request, route, token, candidates).ConfigureAwait(false);
        }

        return Page(_pageComposer.ComposeChoose(new ComposeChooseRequest
        {
            SubjectName = route.DisplayNameOf(route.SenderPickedRecipient),
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
        var subjectName = route.DisplayNameOf(route.SenderPickedRecipient);
        var submission = ReadSubmission(request);

        var round = await _asking
            .AskAsync(route, submission.Chosen, submission.Question)
            .ConfigureAwait(false);

        // The form again, with whatever they typed and ticked still in it. Back to the same page
        // rather than on to a results page with nothing on it.
        APIGatewayProxyResponse Retry(string notice) =>
            Page(_pageComposer.ComposeChoose(new ComposeChooseRequest
            {
                SubjectName = subjectName,
                Candidates = candidates,
                AskToken = token,
                Notice = notice,
                Question = submission.Question,
                Chosen = [.. round.Chosen]
            }));

        return round.Outcome switch
        {
            AskRoundOutcome.NobodyChosen => Retry("Choose at least one person to ask."),
            AskRoundOutcome.QuestionRefused => Retry(AskPageComposer.ExplainRefusal(round.QuestionOutcome)),
            _ => Page(_pageComposer.ComposeAskResults(new ComposeAskResultsRequest
            {
                SubjectName = subjectName,
                Attempts = round.Attempts,
                ReleasedHeldIdeas = round.ReleasedHeldIdeas
            }))
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
            Headers = EmailLinkedPage.Headers(new Dictionary<string, string>
            {
                // Nothing here is worth storing, and a cached Ask page shown after the fact would
                // report an outcome that is no longer true.
                ["Cache-Control"] = "no-store"
            })
        };
}
