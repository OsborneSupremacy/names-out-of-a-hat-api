using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// Leaving: one participant taking themselves out of a gift exchange they did not ask to be in.
/// </summary>
/// <remarks>
/// Until this existed there was no way out. An organizer can type any address into an exchange, the
/// invitation arrives unannounced, and the only recourse was to write to them and hope. Nothing in
/// this application stopped a send either — a bounce or a complaint was recorded and shown, and the
/// next invitation went out regardless.
///
/// Two endpoints for one action, and the split is the security design rather than an accident of
/// REST, for the same reason the Ask is split. Following a link in an email is a GET, and mail
/// security scanners fetch those on delivery. A GET that removed the participant would fire before
/// the reader had opened anything: somebody pulled out of an exchange they had not yet read they
/// were in, everybody else told to disregard a name, and the organizer sent back to the hat. So the
/// GET renders a form, and the POST behind the button on it does the work.
///
/// Not available to organizers, and the way that is enforced is that no leave token is ever issued
/// for one. A flag would have to be checked, and a check can be forgotten; a lookup that finds
/// nothing cannot be. An organizer who somehow reaches this address gets the same page as a guessed
/// token.
///
/// The leaving itself is <see cref="GiftExchangeLeaving"/>'s, which the participant's signed-in page
/// shares; this class is the token and the HTML around it.
/// </remarks>
[UsedImplicitly]
internal class LeaveGiftExchangeService : IApiGatewayHandler
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly GiftExchangeLeaving _leaving;

    private readonly LeavePageComposer _pageComposer;

    // ReSharper disable once ConvertToPrimaryConstructor
    public LeaveGiftExchangeService(
        GiftExchangeProvider giftExchangeProvider,
        GiftExchangeLeaving leaving,
        LeavePageComposer pageComposer
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _leaving = leaving ?? throw new ArgumentNullException(nameof(leaving));
        _pageComposer = pageComposer ?? throw new ArgumentNullException(nameof(pageComposer));
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    )
    {
        // Case preserved. The token is base64url, so "aB" and "Ab" are different tokens, and the
        // ordinary instinct to normalise an identifier out of a URL would break every leave link.
        var token = request.PathParameters is not null
            && request.PathParameters.TryGetValue("token", out var pathToken)
                ? pathToken
                : string.Empty;

        var (found, route) = await _giftExchangeProvider
            .FindLeaveRouteAsync(SecretToken.Hash(token))
            .ConfigureAwait(false);

        // Unknown, spent, or an organizer's guess: one page, and the sameness is the point. The
        // difference between them would tell somebody holding a guessed token whether it named a
        // real participant, which here is worth more than it is on the Ask — a token that resolves
        // is a token that removes somebody.
        if (!found)
            return Page(LeavePageComposer.ComposeUnavailable());

        return request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
            ? await LeaveAsync(request, route).ConfigureAwait(false)
            : Page(_pageComposer.ComposeConfirm(route, token, GiftExchangeLeaving.ShowsConsequences(route.HatStatus)));
    }

    private async Task<APIGatewayProxyResponse> LeaveAsync(APIGatewayProxyRequest request, LeaveRoute route)
    {
        var blockOrganizer = IsTicked(request, LeavePageComposer.BlockOrganizerField);
        var blockAnywhere = IsTicked(request, LeavePageComposer.BlockAnywhereField);

        var left = await _leaving
            .LeaveAsync(route, blockOrganizer, blockAnywhere)
            .ConfigureAwait(false);

        return left
            ? Page(_pageComposer.ComposeLeft(route, blockOrganizer, blockAnywhere))
            : Page(LeavePageComposer.ComposeUnavailable());
    }

    /// <summary>
    /// Whether one checkbox came back ticked.
    /// </summary>
    /// <remarks>
    /// Tolerant, as the Ask's form parsing is: an unreadable body means nothing was ticked, which
    /// is the safe reading of it. These two boxes only ever add a refusal, so failing to see one
    /// costs somebody a refusal they asked for and the page tells them what was recorded; misreading
    /// junk as a tick would opt somebody out of every gift exchange they are ever invited to.
    /// </remarks>
    private static bool IsTicked(APIGatewayProxyRequest request, string field)
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
                return false;
            }
        }

        return HttpUtility.ParseQueryString(body).GetValues(field) is not null;
    }

    /// <summary>
    /// Every outcome is a 200 carrying a page, including the ones that did nothing.
    /// </summary>
    /// <remarks>
    /// The same reasoning as the Ask's: a status code would be read by the scanner that fetched
    /// this before any person did, and there is nobody for a 404 to inform. The reader is a human
    /// looking at a browser tab.
    /// </remarks>
    private static APIGatewayProxyResponse Page(string html) =>
        new()
        {
            StatusCode = (int)HttpStatusCode.OK,
            Body = html,
            Headers = EmailLinkedPage.Headers(new Dictionary<string, string>
            {
                // A cached confirm page shown after the fact would offer to do something that has
                // already been done, and a cached result page would report an outcome twice.
                ["Cache-Control"] = "no-store"
            })
        };
}
