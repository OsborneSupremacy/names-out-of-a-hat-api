namespace GiftExchange.Library.Services;

/// <summary>
/// One gift exchange for somebody taking part in it: what the organizer said, who is in it, and what
/// has been said about gift ideas that the caller may see.
/// </summary>
internal class GetParticipantViewService : IApiGatewayHandler
{
    private readonly ApiGatewayAdapter _adapter;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    public GetParticipantViewService(ApiGatewayAdapter adapter, GiftExchangeProvider giftExchangeProvider)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
    }

    /// <summary>
    /// The caller comes from the authorizer rather than from the <c>{email}</c> segment, which is
    /// there so the route reads the same way the organizer's does.
    /// </summary>
    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync(
            new GetParticipantViewRequest
            {
                ParticipantEmail = request.GetAuthenticatedEmail(),
                HatId = request.GetIdPathParameter()
            },
            GetParticipantViewAsync);

    internal async Task<Result<ParticipantView>> GetParticipantViewAsync(GetParticipantViewRequest request)
    {
        var found = await _giftExchangeProvider
            .GetParticipantViewAsync(request)
            .ConfigureAwait(false);

        if (!found.Exists)
            return new Result<ParticipantView>(
                new KeyNotFoundException($"Hat with id {request.HatId} not found"),
                HttpStatusCode.NotFound);

        var view = found.View.Status == HatStatus.Closed ? found.View : WithOnlyYourPick(found.View);

        return new Result<ParticipantView>(
            view with { GiftIdeas = await GetGiftIdeasAsync(request).ConfigureAwait(false) },
            HttpStatusCode.OK);
    }

    /// <summary>
    /// What the caller may see about gift ideas, and whether they may still share and ask.
    /// </summary>
    /// <remarks>
    /// Read through the same route the email links resolve to, so the rules about who may ask whom
    /// and what is still being accepted are the ones <see cref="GiftIdeaSharing"/> and
    /// <see cref="GiftIdeaAsking"/> apply when the buttons here are pressed. The view has already
    /// found the caller, so a route that does not resolve means only that the exchange changed in
    /// between, and the honest answer then is that there is nothing to show.
    /// </remarks>
    private async Task<ParticipantGiftIdeas> GetGiftIdeasAsync(GetParticipantViewRequest request)
    {
        var (found, route) = await _giftExchangeProvider
            .FindGiftIdeaRouteForParticipantAsync(new FindParticipantRouteRequest
            {
                ParticipantEmail = request.ParticipantEmail,
                HatId = request.HatId
            })
            .ConfigureAwait(false);

        if (!found)
            return ParticipantGiftIdeasDefaults.Empty;

        var canShare = GiftIdeaSharing.AcceptingStatuses.Contains(route.HatStatus);
        var canAsk = canShare && GiftIdeaAsking.CanAsk(route);

        var giftIdeas = await _giftExchangeProvider.GetParticipantGiftIdeasAsync(route).ConfigureAwait(false);

        // Nobody to choose from once nothing can be sent, so the lists are left empty rather than
        // read and then ignored.
        var askCandidates = canAsk
            ? await _giftExchangeProvider.ListAskCandidatesAsync(route.HatId, route.ParticipantId).ConfigureAwait(false)
            : [];

        var offerCandidates = canShare
            ? await _giftExchangeProvider.ListOfferCandidatesAsync(route.HatId, route.ParticipantId).ConfigureAwait(false)
            : [];

        return giftIdeas with
        {
            CanShare = canShare,
            CanAsk = canAsk,
            AskCandidates = askCandidates,
            OfferCandidates = offerCandidates
        };
    }

    /// <summary>
    /// Everybody's pick but the caller's taken out, until the exchange closes.
    /// </summary>
    /// <remarks>
    /// The rule <c>GetHatService.RedactPickedRecipients</c> and <c>ExportHatService.WithoutPicks</c>
    /// apply for the organizer, narrowed by one: the caller already knows who they drew, because
    /// their invitation told them. Emptied rather than replaced with a placeholder, so there is no
    /// string a client could mistake for somebody's name.
    /// </remarks>
    private static ParticipantView WithOnlyYourPick(ParticipantView view) =>
        view with
        {
            Participants =
            [
                .. view.Participants
                    .Select(participant => participant.IsYou
                        ? participant
                        : participant with { PickedRecipient = string.Empty })
            ]
        };
}
