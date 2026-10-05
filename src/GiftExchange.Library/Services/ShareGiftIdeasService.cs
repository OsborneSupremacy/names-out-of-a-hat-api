namespace GiftExchange.Library.Services;

/// <summary>
/// Sharing gift ideas: a participant writing what they would like, or somebody who was asked writing
/// what they think another participant would like.
/// </summary>
/// <remarks>
/// Two endpoints for one action, for the reason <see cref="AskForGiftIdeasService"/> gives. The
/// button lives in an email, so following it is a GET, and mail security scanners fetch links in
/// delivered mail. The GET only renders the form; the POST behind its button stores and forwards.
///
/// The token in the link is the whole credential, as it is for the Ask and for leaving. That is
/// why an address correction revokes every link the old address was sent
/// (<see cref="GiftExchangeProvider.RevokeGiftIdeaLinksAsync"/>): whoever reads the wrong inbox
/// has the link.
///
/// Both kinds of submission come through here, told apart by which table the token resolves in.
/// What happens to a submission once its route is found is <see cref="GiftIdeaSharing"/>'s, which
/// the participant's signed-in page shares; this class is the token and the HTML around it.
/// </remarks>
[UsedImplicitly]
internal class ShareGiftIdeasService : IApiGatewayHandler
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly GiftIdeaSharing _sharing;

    private readonly ShareIdeasPageComposer _pageComposer;

    public ShareGiftIdeasService(
        GiftExchangeProvider giftExchangeProvider,
        GiftIdeaSharing sharing,
        ShareIdeasPageComposer pageComposer
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _sharing = sharing ?? throw new ArgumentNullException(nameof(sharing));
        _pageComposer = pageComposer ?? throw new ArgumentNullException(nameof(pageComposer));
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    )
    {
        // Case preserved, as in the Ask: the token is base64url, so "aB" and "Ab" are different.
        var token = request.PathParameters is not null
            && request.PathParameters.TryGetValue("token", out var pathToken)
                ? pathToken
                : string.Empty;

        var (found, route) = await ResolveRouteAsync(SecretToken.Hash(token)).ConfigureAwait(false);

        // One page for both, so that a guessed token cannot be told apart from a finished exchange.
        if (!found || !GiftIdeaSharing.AcceptingStatuses.Contains(route.HatStatus))
            return Page(ShareIdeasPageComposer.ComposeUnavailable());

        if (!request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return await ShowFormAsync(route, token).ConfigureAwait(false);

        // Whatever becomes of the submission: somebody pressed the button, and that is what the
        // invitation reminder wants to know. An ask's token is in another table and matches nothing
        // here, which is right — answering somebody else's ask says nothing about one's own
        // invitation.
        await _giftExchangeProvider.MarkGiftIdeaTokenUsedAsync(SecretToken.Hash(token)).ConfigureAwait(false);

        return await ShareAsync(route, token, ParseSubmission(request)).ConfigureAwait(false);
    }

    /// <summary>
    /// The form, filled in with whatever was shared last from this route.
    /// </summary>
    private async Task<APIGatewayProxyResponse> ShowFormAsync(GiftIdeaRoute route, string token)
    {
        if (route.IsContribution)
        {
            var answer = await _giftExchangeProvider
                .GetLatestContributedGiftIdeaAsync(route.AskId)
                .ConfigureAwait(false);

            return Page(_pageComposer.ComposeForm(new ComposeShareIdeasFormRequest
            {
                Route = route,
                Token = token,
                Ideas = answer,
                Notice = string.Empty,
                HasSharedBefore = !string.IsNullOrEmpty(answer),
                // Never offered on this path: whoever is writing was asked, so there is nothing to
                // wait for.
                HoldUntilAsked = false,
                HasSharedOutrightBefore = false
            }));
        }

        var latest = await _giftExchangeProvider
            .GetLatestGiftIdeaAsync(route.ParticipantId)
            .ConfigureAwait(false);

        return Page(_pageComposer.ComposeForm(new ComposeShareIdeasFormRequest
        {
            Route = route,
            Token = token,
            Ideas = latest.Ideas,
            Notice = string.Empty,
            HasSharedBefore = !string.IsNullOrEmpty(latest.Ideas),
            // The standing choice, read back off what is stored. Whether anything has since been
            // released is deliberately not consulted: a box that changed on its own would tell the
            // writer that somebody had asked about them.
            HoldUntilAsked = latest.HoldUntilAsked,
            HasSharedOutrightBefore = latest.HasSharedOutrightBefore
        }));
    }

    /// <summary>
    /// Shares the submission, or hands the form back with the reason it was refused.
    /// </summary>
    private async Task<APIGatewayProxyResponse> ShareAsync(
        GiftIdeaRoute route,
        string token,
        SharedIdeasSubmission submission
    )
    {
        // Worked out here as well as in GiftIdeaSharing, because both pages below describe it.
        var holdUntilAsked = submission.HoldUntilAsked && !route.IsContribution;

        var outcome = await _sharing
            .ShareAsync(route, submission.Ideas, holdUntilAsked)
            .ConfigureAwait(false);

        if (outcome != GiftIdeaSubmissionOutcome.Shared)
            return Page(_pageComposer.ComposeForm(new ComposeShareIdeasFormRequest
            {
                Route = route,
                Token = token,
                Ideas = submission.Ideas,
                Notice = ShareIdeasPageComposer.ExplainRefusal(outcome),
                HasSharedBefore = false,
                // Handed straight back. A box that came back unticked would make the corrected
                // retry an immediate send, which is the one mistake on this page that cannot be
                // taken back.
                HoldUntilAsked = holdUntilAsked,
                HasSharedOutrightBefore = false
            }));

        return Page(_pageComposer.ComposeShared(new ComposeSharedIdeasRequest
        {
            Route = route,
            Ideas = submission.Ideas,
            HoldUntilAsked = holdUntilAsked
        }));
    }

    /// <summary>
    /// Finds what a link routes to, whichever of the two kinds of token it carries.
    /// </summary>
    /// <remarks>
    /// The participant's own kind is tried first because it is much the commoner of the two. The
    /// token spaces do not overlap, so the order is a matter of cost rather than of precedence.
    /// </remarks>
    private async Task<(bool found, GiftIdeaRoute route)> ResolveRouteAsync(string tokenHash)
    {
        var own = await _giftExchangeProvider.FindGiftIdeaRouteAsync(tokenHash).ConfigureAwait(false);

        return own.found
            ? own
            : await _giftExchangeProvider.FindGiftIdeaContributionRouteAsync(tokenHash).ConfigureAwait(false);
    }

    /// <summary>
    /// What the form posted: the text, trimmed and with line endings made consistent, and whether the
    /// writer asked for it to be held back.
    /// </summary>
    /// <remarks>
    /// Reading the body itself is <see cref="FormBody"/>'s job, shared with the page for offering
    /// ideas about somebody else. What stays here is what this form means by what it found.
    ///
    /// Browsers post a textarea's line breaks as CRLF. Stored as LF, so the forward and the echo
    /// break lines the same way whatever sent them. An unreadable body arrives as an empty one,
    /// which the content policy then reports as "write something".
    ///
    /// The checkbox is read by whether its field is there at all, which is what a browser says about
    /// an unticked box — it sends nothing. Its value is not examined: "on" is a default rather than a
    /// contract, and a body this cannot make sense of should err towards holding ideas back rather
    /// than towards sending them.
    /// </remarks>
    private static SharedIdeasSubmission ParseSubmission(APIGatewayProxyRequest request)
    {
        var fields = FormBody.Read(request);

        return new SharedIdeasSubmission(
            fields.First(ShareIdeasPageComposer.IdeasField).Replace("\r\n", "\n").Replace('\r', '\n').Trim(),
            fields.Has(ShareIdeasPageComposer.HoldUntilAskedField));
    }

    /// <summary>What the two fields carry, before the text has been tidied.</summary>
    /// <remarks>
    /// Kept inside this class rather than put in Messaging, because it never crosses a boundary: it
    /// exists so that the two answers this form gives travel together.
    /// </remarks>
    private readonly record struct SharedIdeasSubmission(string Ideas, bool HoldUntilAsked);

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
                // A cached form would show ideas that have since been replaced.
                ["Cache-Control"] = "no-store"
            })
        };
}
