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
/// <see cref="GiftIdeaOffering"/>, which does the work and which the participant's signed-in page
/// shares, and <see cref="OfferIdeasPageComposer.ComposeShared"/>.
/// </remarks>
[UsedImplicitly]
internal class OfferGiftIdeasService : IApiGatewayHandler
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly GiftIdeaOffering _offering;

    private readonly OfferIdeasPageComposer _pageComposer;

    public OfferGiftIdeasService(
        GiftExchangeProvider giftExchangeProvider,
        GiftIdeaOffering offering,
        OfferIdeasPageComposer pageComposer
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _offering = offering ?? throw new ArgumentNullException(nameof(offering));
        _pageComposer = pageComposer ?? throw new ArgumentNullException(nameof(pageComposer));
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
        if (!found || !GiftIdeaSharing.AcceptingStatuses.Contains(route.HatStatus))
            return Page(ShareIdeasPageComposer.ComposeUnavailable());

        var candidates = await _giftExchangeProvider
            .ListOfferCandidatesAsync(route.HatId, route.ParticipantId)
            .ConfigureAwait(false);

        // An exchange of two, where the only other person is the reader's own pick. There is
        // nobody to write about, and a form with no names in it would be a puzzle rather than a
        // page.
        if (candidates.IsEmpty)
            return Page(ShareIdeasPageComposer.ComposeUnavailable());

        if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            // Whatever becomes of the offer: somebody pressed the button, and that is what the
            // invitation reminder wants to know.
            await _giftExchangeProvider.MarkGiftIdeaTokenUsedAsync(SecretToken.Hash(token)).ConfigureAwait(false);

            return await OfferAsync(route, token, candidates, ParseSubmission(request)).ConfigureAwait(false);
        }

        return Page(_pageComposer.ComposeForm(new ComposeOfferIdeasFormRequest
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
    /// Passes the offer on, or hands the form back with the reason it was not.
    /// </summary>
    private async Task<APIGatewayProxyResponse> OfferAsync(
        GiftIdeaRoute route,
        string token,
        ImmutableList<OfferCandidate> candidates,
        OfferedIdeasSubmission submission
    )
    {
        var result = await _offering
            .OfferAsync(route, submission.SubjectParticipantId, submission.Ideas)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            OfferOutcome.NobodyChosen =>
                Form(token, candidates, Guid.Empty, submission.Ideas, "Choose who these ideas are about."),
            OfferOutcome.Refused =>
                Form(token, candidates, result.SubjectParticipantId, submission.Ideas, ShareIdeasPageComposer.ExplainRefusal(result.Refusal)),
            OfferOutcome.AlreadyOffered =>
                Page(_pageComposer.ComposeAlreadyShared(result.SubjectName, result.PreviouslyOfferedAt)),
            _ => Page(_pageComposer.ComposeShared(new ComposeOfferedIdeasRequest
            {
                SubjectName = result.SubjectName,
                Ideas = submission.Ideas
            }))
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
            Headers = EmailLinkedPage.Headers(new Dictionary<string, string>
            {
                ["Cache-Control"] = "no-store"
            })
        };
}
