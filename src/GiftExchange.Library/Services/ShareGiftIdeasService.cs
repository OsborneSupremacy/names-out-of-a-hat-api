using System.Web;
using MimeKit;

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
/// Every check is the same for both; what differs is where the text is stored and who it goes to.
/// </remarks>
[UsedImplicitly]
internal class ShareGiftIdeasService : IApiGatewayHandler
{
    /// <summary>Statuses during which there is still somebody to share ideas with.</summary>
    private static readonly ImmutableList<string> AcceptingStatuses =
        [HatStatus.NamesAssigned, HatStatus.InvitationsSent, HatStatus.CooledOff];

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly GiftIdeaContentPolicy _contentPolicy;

    private readonly IContentModerationService _contentModerationService;

    private readonly GiftIdeaEmailCompositionService _composer;

    private readonly ShareIdeasPageComposer _pageComposer;

    private readonly AutomaticEmailSender _sender;

    private readonly ILogger<ShareGiftIdeasService> _logger;

    public ShareGiftIdeasService(
        GiftExchangeProvider giftExchangeProvider,
        GiftIdeaContentPolicy contentPolicy,
        IContentModerationService contentModerationService,
        GiftIdeaEmailCompositionService composer,
        ShareIdeasPageComposer pageComposer,
        AutomaticEmailSender sender,
        ILogger<ShareGiftIdeasService> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
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
        // Case preserved, as in the Ask: the token is base64url, so "aB" and "Ab" are different.
        var token = request.PathParameters is not null
            && request.PathParameters.TryGetValue("token", out var pathToken)
                ? pathToken
                : string.Empty;

        var (found, route) = await ResolveRouteAsync(SecretToken.Hash(token)).ConfigureAwait(false);

        // One page for both, so that a guessed token cannot be told apart from a finished exchange.
        if (!found || !AcceptingStatuses.Contains(route.HatStatus))
            return Page(ShareIdeasPageComposer.ComposeUnavailable());

        return request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
            ? await ShareAsync(route, token, ParseIdeas(request)).ConfigureAwait(false)
            : await ShowFormAsync(route, token).ConfigureAwait(false);
    }

    /// <summary>
    /// The form, filled in with whatever was shared last from this route.
    /// </summary>
    private async Task<APIGatewayProxyResponse> ShowFormAsync(GiftIdeaRoute route, string token)
    {
        var latest = route.IsContribution
            ? await _giftExchangeProvider.GetLatestContributedGiftIdeaAsync(route.AskId).ConfigureAwait(false)
            : await _giftExchangeProvider.GetLatestGiftIdeaAsync(route.ParticipantId).ConfigureAwait(false);

        return Page(_pageComposer.ComposeForm(new ComposeShareIdeasFormRequest
        {
            Route = route,
            Token = token,
            Ideas = latest,
            Notice = string.Empty,
            HasSharedBefore = !string.IsNullOrEmpty(latest)
        }));
    }

    /// <summary>
    /// Checks the submission, and either shares it or hands the form back with the reason.
    /// </summary>
    /// <remarks>
    /// Stored before anything is sent. If sending fails after this, the submission still exists;
    /// the reverse would lose what somebody wrote.
    /// </remarks>
    private async Task<APIGatewayProxyResponse> ShareAsync(GiftIdeaRoute route, string token, string ideas)
    {
        var outcome = await CheckAsync(ideas, route).ConfigureAwait(false);

        if (outcome != GiftIdeaSubmissionOutcome.Shared)
        {
            _logger.LogInformation("Refused a gift ideas submission: {Outcome}", outcome);

            return Page(_pageComposer.ComposeForm(new ComposeShareIdeasFormRequest
            {
                Route = route,
                Token = token,
                Ideas = ideas,
                Notice = ShareIdeasPageComposer.ExplainRefusal(outcome),
                HasSharedBefore = false
            }));
        }

        await StoreAsync(route, ideas).ConfigureAwait(false);

        await ForwardAsync(route, ideas).ConfigureAwait(false);

        return Page(_pageComposer.ComposeShared(route, ideas));
    }

    /// <summary>
    /// Whether there is anything about this submission that stops it being passed on.
    /// </summary>
    /// <remarks>
    /// Cheapest first, so text refused on a rule this application can apply itself never reaches
    /// Comprehend.
    /// </remarks>
    private async Task<GiftIdeaSubmissionOutcome> CheckAsync(string ideas, GiftIdeaRoute route)
    {
        // The writer's own pick, even on a contribution about somebody else. The check is about
        // what the writer must not leak, and the person reading this knows who wrote it.
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

    /// <summary>
    /// Writes the submission to whichever table it belongs in.
    /// </summary>
    /// <remarks>
    /// Two tables, because the two are not the same claim. What somebody says about themselves is
    /// theirs; what somebody says about another participant is a suggestion made to the one person
    /// who asked for it, and must never be read back as the subject's own words.
    /// </remarks>
    private Task<Guid> StoreAsync(GiftIdeaRoute route, string ideas) =>
        route.IsContribution switch
        {
            true => _giftExchangeProvider.AddContributedGiftIdeaAsync(route.AskId, ideas),
            false => _giftExchangeProvider.AddGiftIdeaAsync(route.ParticipantId, ideas)
        };

    private Task ForwardAsync(GiftIdeaRoute route, string ideas)
    {
        // Nobody has drawn this participant, so there is nobody to forward to. The submission is
        // already stored. A contribution always has somebody — the asker — so this is only ever
        // reached on the ordinary path.
        if (string.IsNullOrWhiteSpace(route.Giver.Email))
        {
            _logger.LogInformation("Stored a gift ideas submission with nobody yet to forward it to.");
            return Task.CompletedTask;
        }

        // Subject and body chosen together, so that the two cannot be made to disagree about which
        // kind of message this is.
        var (subject, body) = route.IsContribution switch
        {
            true => (
                GiftIdeaEmailCompositionService.ContributionForwardSubject(route.Sender.Name, route.Subject.Name),
                _composer.ComposeContributionForward(route.Sender.Name, route.Subject.Name, route.HatName, ideas)),
            false => (
                GiftIdeaEmailCompositionService.ForwardSubject(route.Sender.Name),
                _composer.ComposeForward(route.Sender.Name, route.HatName, ideas))
        };

        return _sender.SendAsync(route.Giver.Email, subject, body);
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
    /// The text from the form, trimmed and with line endings made consistent.
    /// </summary>
    /// <remarks>
    /// The form posts multipart/form-data, for the size reason
    /// <see cref="GiftIdeaContentPolicy.MaxLength"/> gives. A URL-encoded body is read too, since it
    /// is what a form without an enctype sends and there is no reason to refuse one.
    ///
    /// Browsers post a textarea's line breaks as CRLF. Stored as LF, so the forward and the echo
    /// break lines the same way whatever sent them. An unreadable body is treated as an empty one,
    /// which the content policy then reports as "write something".
    /// </remarks>
    private static string ParseIdeas(APIGatewayProxyRequest request)
    {
        byte[] body;

        try
        {
            body = request.IsBase64Encoded
                ? Convert.FromBase64String(request.Body ?? string.Empty)
                : Encoding.UTF8.GetBytes(request.Body ?? string.Empty);
        }
        catch (FormatException)
        {
            return string.Empty;
        }

        var contentType = FindHeader(request, "Content-Type");

        var ideas = contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            ? ReadMultipartField(body, contentType)
            : HttpUtility.ParseQueryString(Encoding.UTF8.GetString(body)).Get(ShareIdeasPageComposer.IdeasField)
              ?? string.Empty;

        return ideas.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    /// <summary>
    /// The ideas field out of a multipart/form-data body, or the empty string.
    /// </summary>
    /// <remarks>
    /// MimeKit is already here for sending mail, and a form post is a MIME multipart with a
    /// Content-Disposition on each part. The request's Content-Type header carries the boundary,
    /// so it is put back in front of the body to make a complete entity to parse.
    ///
    /// Decoded as UTF-8 explicitly. Browsers send form fields in the page's encoding and name no
    /// charset on the part, and the page declares UTF-8.
    /// </remarks>
    private static string ReadMultipartField(byte[] body, string contentType)
    {
        try
        {
            using var stream = new MemoryStream();
            stream.Write(Encoding.ASCII.GetBytes($"Content-Type: {contentType}\r\n\r\n"));
            stream.Write(body);
            stream.Position = 0;

            if (MimeEntity.Load(stream) is not Multipart multipart)
                return string.Empty;

            var field = multipart
                .OfType<TextPart>()
                .FirstOrDefault(part =>
                    part.ContentDisposition is not null
                    && part.ContentDisposition.Parameters.TryGetValue("name", out string? name)
                    && name == ShareIdeasPageComposer.IdeasField);

            return field?.GetText(Encoding.UTF8) ?? string.Empty;
        }
        catch (Exception exception) when (exception is FormatException or ParseException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// A request header by name, ignoring case, or the empty string.
    /// </summary>
    /// <remarks>
    /// Case-insensitive because API Gateway passes header names through as the client sent them,
    /// and HTTP/2 clients send them lower-cased.
    /// </remarks>
    private static string FindHeader(APIGatewayProxyRequest request, string name) =>
        request.Headers?
            .FirstOrDefault(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Value
        ?? string.Empty;

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
                // A cached form would show ideas that have since been replaced.
                ["Cache-Control"] = "no-store"
            }
        };
}
