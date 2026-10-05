using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// Gift ideas from the participant's own signed-in page: sharing their own, answering an ask about
/// somebody else, offering ideas unasked, and asking about their pick.
/// </summary>
/// <remarks>
/// Each of these already exists behind a button in an email, and each does exactly what that button
/// does — the work is <see cref="GiftIdeaSharing"/>'s, <see cref="GiftIdeaAsking"/>'s and
/// <see cref="GiftIdeaOffering"/>'s, shared with the token pages. What differs is only how the
/// participant is found. There, the token is the whole credential; here, it is the session, and the
/// participant is whichever row the signed-in address holds in the exchange named.
///
/// None of the GET-renders, POST-acts split the email pages need applies. That split exists because
/// mail scanners follow links in delivered mail, and nothing follows a link to these: they are
/// authenticated JSON endpoints called by the page, with the address taken from the session.
///
/// Refusals carry the same words the email pages show, decoded from the HTML those pages are
/// written in, so the two doors explain a refusal identically.
///
/// One handler for four resources rather than four handlers, because they share every dependency
/// and each is a few lines over the workflow it calls.
/// </remarks>
internal class ParticipantGiftIdeasService : IApiGatewayHandler
{
    private const string ShareKey = "put/participating/ideas";

    private const string AnswerKey = "put/participating/answer";

    private const string OfferKey = "post/participating/offer";

    private const string AskKey = "post/participating/ask";

    private readonly ApiGatewayAdapter _adapter;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly GiftIdeaSharing _sharing;

    private readonly GiftIdeaAsking _asking;

    private readonly GiftIdeaOffering _offering;

    public ParticipantGiftIdeasService(
        ApiGatewayAdapter adapter,
        GiftExchangeProvider giftExchangeProvider,
        GiftIdeaSharing sharing,
        GiftIdeaAsking asking,
        GiftIdeaOffering offering
    )
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _sharing = sharing ?? throw new ArgumentNullException(nameof(sharing));
        _asking = asking ?? throw new ArgumentNullException(nameof(asking));
        _offering = offering ?? throw new ArgumentNullException(nameof(offering));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    ) =>
        $"{request.HttpMethod}{request.Resource}".ToLowerInvariant() switch
        {
            ShareKey => _adapter.AdaptAsync<ShareGiftIdeasRequest, StatusCodeOnlyResponse>(request, ShareAsync),
            AnswerKey => _adapter.AdaptAsync<AnswerGiftIdeaAskRequest, StatusCodeOnlyResponse>(request, AnswerAsync),
            OfferKey => _adapter.AdaptAsync<OfferGiftIdeasRequest, StatusCodeOnlyResponse>(request, OfferAsync),
            AskKey => _adapter.AdaptAsync<AskForGiftIdeasRequest, AskForGiftIdeasResponse>(request, AskAsync),
            _ => Task.FromResult(ProxyResponseBuilder.Build(HttpStatusCode.NotFound))
        };

    /// <summary>The caller's own ideas, for whoever drew them.</summary>
    internal async Task<Result<StatusCodeOnlyResponse>> ShareAsync(ShareGiftIdeasRequest request)
    {
        var (found, route) = await FindOwnRouteAsync(request.ParticipantEmail, request.HatId).ConfigureAwait(false);

        if (!found)
            return NotFound<StatusCodeOnlyResponse>();

        if (!GiftIdeaSharing.AcceptingStatuses.Contains(route.HatStatus))
            return NoLongerAccepting<StatusCodeOnlyResponse>();

        var outcome = await _sharing
            .ShareAsync(route, Tidy(request.Ideas), request.HoldUntilAsked)
            .ConfigureAwait(false);

        return outcome == GiftIdeaSubmissionOutcome.Shared
            ? Done()
            : Refused<StatusCodeOnlyResponse>(ShareIdeasPageComposer.ExplainRefusal(outcome));
    }

    /// <summary>The caller answering somebody's ask about a third participant.</summary>
    internal async Task<Result<StatusCodeOnlyResponse>> AnswerAsync(AnswerGiftIdeaAskRequest request)
    {
        var (found, route) = await _giftExchangeProvider
            .FindGiftIdeaContributionRouteForHelperAsync(new FindHelperAskRouteRequest
            {
                HelperEmail = request.ParticipantEmail,
                HatId = request.HatId,
                AskId = request.AskId
            })
            .ConfigureAwait(false);

        if (!found)
            return NotFound<StatusCodeOnlyResponse>();

        if (!GiftIdeaSharing.AcceptingStatuses.Contains(route.HatStatus))
            return NoLongerAccepting<StatusCodeOnlyResponse>();

        // Never held: whoever is answering was asked, so there is nothing to wait for.
        var outcome = await _sharing
            .ShareAsync(route, Tidy(request.Ideas), holdUntilAsked: false)
            .ConfigureAwait(false);

        return outcome == GiftIdeaSubmissionOutcome.Shared
            ? Done()
            : Refused<StatusCodeOnlyResponse>(ShareIdeasPageComposer.ExplainRefusal(outcome));
    }

    /// <summary>
    /// The caller offering ideas about somebody other than their pick, unasked.
    /// </summary>
    /// <remarks>
    /// Success says nothing about whether anything was sent, as the email page's confirmation does
    /// not. The weekly limit is reported, because it is about the caller's own earlier offer and
    /// says nothing about anybody else.
    /// </remarks>
    internal async Task<Result<StatusCodeOnlyResponse>> OfferAsync(OfferGiftIdeasRequest request)
    {
        var (found, route) = await FindOwnRouteAsync(request.ParticipantEmail, request.HatId).ConfigureAwait(false);

        if (!found)
            return NotFound<StatusCodeOnlyResponse>();

        if (!GiftIdeaSharing.AcceptingStatuses.Contains(route.HatStatus))
            return NoLongerAccepting<StatusCodeOnlyResponse>();

        var result = await _offering
            .OfferAsync(route, request.SubjectParticipantId, Tidy(request.Ideas))
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            OfferOutcome.Offered => Done(),
            OfferOutcome.NobodyChosen => Failure<StatusCodeOnlyResponse>(
                "Choose who these ideas are about.", HttpStatusCode.BadRequest),
            OfferOutcome.Refused => Refused<StatusCodeOnlyResponse>(ShareIdeasPageComposer.ExplainRefusal(result.Refusal)),
            _ => Failure<StatusCodeOnlyResponse>(
                ExplainAlreadyOffered(result.SubjectName, result.PreviouslyOfferedAt), HttpStatusCode.TooManyRequests)
        };
    }

    /// <summary>The caller asking for gift ideas about their pick.</summary>
    internal async Task<Result<AskForGiftIdeasResponse>> AskAsync(AskForGiftIdeasRequest request)
    {
        var (found, route) = await FindOwnRouteAsync(request.ParticipantEmail, request.HatId).ConfigureAwait(false);

        if (!found)
            return NotFound<AskForGiftIdeasResponse>();

        if (!GiftIdeaAsking.CanAsk(route))
            return NoLongerAccepting<AskForGiftIdeasResponse>();

        var round = await _asking
            .AskAsync(route, request.ParticipantIds, Tidy(request.Question))
            .ConfigureAwait(false);

        return round.Outcome switch
        {
            AskRoundOutcome.NobodyChosen => Failure<AskForGiftIdeasResponse>(
                "Choose at least one person to ask.", HttpStatusCode.BadRequest),
            AskRoundOutcome.QuestionRefused => Refused<AskForGiftIdeasResponse>(
                AskPageComposer.ExplainRefusal(round.QuestionOutcome)),
            _ => new Result<AskForGiftIdeasResponse>(
                new AskForGiftIdeasResponse
                {
                    Attempts = round.Attempts,
                    ReleasedHeldIdeas = round.ReleasedHeldIdeas
                },
                HttpStatusCode.OK)
        };
    }

    private Task<(bool found, GiftIdeaRoute route)> FindOwnRouteAsync(string participantEmail, Guid hatId) =>
        _giftExchangeProvider.FindGiftIdeaRouteForParticipantAsync(new FindParticipantRouteRequest
        {
            ParticipantEmail = participantEmail,
            HatId = hatId
        });

    /// <summary>
    /// Trimmed, with line endings made consistent, as the email pages store theirs: a textarea posts
    /// CRLF in some browsers, and the forward and the read-back should break lines the same way
    /// whichever sent them.
    /// </summary>
    private static string Tidy(string text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static string ExplainAlreadyOffered(string subjectName, DateTimeOffset previouslyOfferedAt) =>
        previouslyOfferedAt == DateTimeOffset.MinValue
            ? $"You shared ideas about {subjectName} recently, so we haven't passed this on. You can share ideas about them again a week after that."
            : $"You shared ideas about {subjectName} on {previouslyOfferedAt:d MMMM yyyy}, so we haven't passed this on. You can share ideas about them again a week after that.";

    private static Result<StatusCodeOnlyResponse> Done() =>
        new(new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.NoContent }, HttpStatusCode.NoContent);

    /// <summary>
    /// Not in the exchange, not yet told about it, or no such exchange: one answer for all three, as
    /// the participant view gives.
    /// </summary>
    private static Result<T> NotFound<T>() =>
        Failure<T>("Gift exchange not found.", HttpStatusCode.NotFound);

    private static Result<T> NoLongerAccepting<T>() =>
        Failure<T>("This gift exchange has finished, so gift ideas can't be shared any more.", HttpStatusCode.Conflict);

    /// <summary>
    /// A refusal in the words the email page uses. Those are written for HTML, entities and all, and
    /// carry no markup beyond entities, so decoding them is all it takes to make them plain text.
    /// </summary>
    private static Result<T> Refused<T>(string htmlMessage) =>
        Failure<T>(HttpUtility.HtmlDecode(htmlMessage), HttpStatusCode.UnprocessableEntity);

    private static Result<T> Failure<T>(string message, HttpStatusCode statusCode) =>
        new(new InvalidOperationException(message), statusCode);
}
