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
///
/// A participant writing about themselves may also ask for their words to be held back until the
/// person who drew them asks for gift ideas. That is the one thing this handler stores and does not
/// send — released by <see cref="AskForGiftIdeasService"/> when the ask comes, or here if the ask
/// came first. Which of the two happened is never shown to the writer: see
/// <see cref="ForwardIfAlreadyAskedAsync"/>.
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
            ? await ShareAsync(route, token, ParseSubmission(request)).ConfigureAwait(false)
            : await ShowFormAsync(route, token).ConfigureAwait(false);
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
    /// Checks the submission, and either shares it or hands the form back with the reason.
    /// </summary>
    /// <remarks>
    /// Stored before anything is sent. If sending fails after this, the submission still exists;
    /// the reverse would lose what somebody wrote.
    /// </remarks>
    private async Task<APIGatewayProxyResponse> ShareAsync(
        GiftIdeaRoute route,
        string token,
        SharedIdeasSubmission submission
    )
    {
        // Ignored outright on a contribution rather than merely unoffered, so a hand-made post
        // cannot hold back ideas that were asked for.
        var holdUntilAsked = submission.HoldUntilAsked && !route.IsContribution;

        var outcome = await CheckAsync(submission.Ideas, route).ConfigureAwait(false);

        if (outcome != GiftIdeaSubmissionOutcome.Shared)
        {
            _logger.LogInformation("Refused a gift ideas submission: {Outcome}", outcome);

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
        }

        await StoreAsync(route, submission.Ideas, holdUntilAsked).ConfigureAwait(false);

        if (holdUntilAsked)
            await ForwardIfAlreadyAskedAsync(route, submission.Ideas).ConfigureAwait(false);
        else
            await ForwardAsync(route, submission.Ideas).ConfigureAwait(false);

        return Page(_pageComposer.ComposeShared(new ComposeSharedIdeasRequest
        {
            Route = route,
            Ideas = submission.Ideas,
            HoldUntilAsked = holdUntilAsked
        }));
    }

    /// <summary>
    /// Passes a held submission on after all, when the person it is for has already asked.
    /// </summary>
    /// <remarks>
    /// Asking and writing can happen in either order, and a submission written after the ask is owed
    /// to somebody who is waiting for it. Nothing about this reaches the page: the writer is told the
    /// same thing either way, because the difference between the two is the fact that their giver
    /// asked, and that is precisely what asking somebody else about them was meant to keep quiet.
    ///
    /// The release is stamped so that the next round of asking does not send the same text again.
    /// </remarks>
    private async Task ForwardIfAlreadyAskedAsync(GiftIdeaRoute route, string ideas)
    {
        // Nobody has drawn them, so nobody can have asked. The submission is already stored.
        if (route.GiverParticipantId == Guid.Empty)
            return;

        var asked = await _giftExchangeProvider
            .HasAskedForGiftIdeasAsync(new HasAskedForGiftIdeasRequest
            {
                AskerParticipantId = route.GiverParticipantId,
                SubjectParticipantId = route.ParticipantId
            })
            .ConfigureAwait(false);

        if (!asked)
        {
            _logger.LogInformation("Held a gift ideas submission until somebody asks for it.");
            return;
        }

        await ForwardAsync(route, ideas).ConfigureAwait(false);

        await _giftExchangeProvider
            .MarkGiftIdeaEnquiryReleasedAsync(new MarkGiftIdeaEnquiryReleasedRequest
            {
                AskerParticipantId = route.GiverParticipantId,
                SubjectParticipantId = route.ParticipantId,
                ReleasedAt = DateTimeOffset.UtcNow
            })
            .ConfigureAwait(false);
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
    private Task<Guid> StoreAsync(GiftIdeaRoute route, string ideas, bool holdUntilAsked) =>
        route.IsContribution switch
        {
            true => _giftExchangeProvider.AddContributedGiftIdeaAsync(route.AskId, ideas),
            false => _giftExchangeProvider.AddGiftIdeaAsync(new AddGiftIdeaRequest
            {
                ParticipantId = route.ParticipantId,
                Ideas = ideas,
                HoldUntilAsked = holdUntilAsked
            })
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
    /// What the form posted: the text, trimmed and with line endings made consistent, and whether the
    /// writer asked for it to be held back.
    /// </summary>
    /// <remarks>
    /// The form posts multipart/form-data, for the size reason
    /// <see cref="GiftIdeaContentPolicy.MaxLength"/> gives. A URL-encoded body is read too, since it
    /// is what a form without an enctype sends and there is no reason to refuse one.
    ///
    /// Browsers post a textarea's line breaks as CRLF. Stored as LF, so the forward and the echo
    /// break lines the same way whatever sent them. An unreadable body is treated as an empty one,
    /// which the content policy then reports as "write something".
    ///
    /// The checkbox is read by whether its field is there at all, which is what a browser says about
    /// an unticked box — it sends nothing. Its value is not examined: "on" is a default rather than a
    /// contract, and a body this cannot make sense of should err towards holding ideas back rather
    /// than towards sending them.
    /// </remarks>
    private static SharedIdeasSubmission ParseSubmission(APIGatewayProxyRequest request)
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
            return Nothing;
        }

        var contentType = FindHeader(request, "Content-Type");

        var fields = contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            ? ReadMultipartFields(body, contentType)
            : ReadUrlEncodedFields(body);

        return new SharedIdeasSubmission(
            fields.Ideas.Replace("\r\n", "\n").Replace('\r', '\n').Trim(),
            fields.HoldUntilAsked);
    }

    /// <summary>What the two fields carry, before the text has been tidied.</summary>
    /// <remarks>
    /// Kept inside this class rather than put in Messaging, because it never crosses a boundary: it
    /// exists so that one pass over the body answers both questions, and the parse of a multipart
    /// body is expensive enough not to want twice.
    /// </remarks>
    private readonly record struct SharedIdeasSubmission(string Ideas, bool HoldUntilAsked);

    /// <summary>A body that said nothing, which the content policy reports as "write something".</summary>
    private static SharedIdeasSubmission Nothing => new(string.Empty, false);

    /// <summary>
    /// Both fields out of a multipart/form-data body.
    /// </summary>
    /// <remarks>
    /// MimeKit is already here for sending mail, and a form post is a MIME multipart with a
    /// Content-Disposition on each part. The request's Content-Type header carries the boundary,
    /// so it is put back in front of the body to make a complete entity to parse.
    ///
    /// Decoded as UTF-8 explicitly. Browsers send form fields in the page's encoding and name no
    /// charset on the part, and the page declares UTF-8.
    /// </remarks>
    private static SharedIdeasSubmission ReadMultipartFields(byte[] body, string contentType)
    {
        try
        {
            using var stream = new MemoryStream();
            stream.Write(Encoding.ASCII.GetBytes($"Content-Type: {contentType}\r\n\r\n"));
            stream.Write(body);
            stream.Position = 0;

            if (MimeEntity.Load(stream) is not Multipart multipart)
                return Nothing;

            var parts = multipart.OfType<TextPart>().ToList();

            var ideas = FindPart(parts, ShareIdeasPageComposer.IdeasField);

            return new SharedIdeasSubmission(
                ideas?.GetText(Encoding.UTF8) ?? string.Empty,
                FindPart(parts, ShareIdeasPageComposer.HoldUntilAskedField) is not null);
        }
        catch (Exception exception) when (exception is FormatException or ParseException)
        {
            return Nothing;
        }
    }

    private static TextPart? FindPart(IEnumerable<TextPart> parts, string name) =>
        parts.FirstOrDefault(part =>
            part.ContentDisposition is not null
            && part.ContentDisposition.Parameters.TryGetValue("name", out string? partName)
            && partName == name);

    /// <summary>
    /// Both fields out of a URL-encoded body, which is what a form with no enctype sends.
    /// </summary>
    private static SharedIdeasSubmission ReadUrlEncodedFields(byte[] body)
    {
        var fields = HttpUtility.ParseQueryString(Encoding.UTF8.GetString(body));

        return new SharedIdeasSubmission(
            fields.Get(ShareIdeasPageComposer.IdeasField) ?? string.Empty,
            fields.AllKeys.Contains(ShareIdeasPageComposer.HoldUntilAskedField));
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
