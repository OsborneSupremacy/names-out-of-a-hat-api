namespace GiftExchange.Library.Services;

/// <summary>
/// One gift exchange, read-only, for somebody taking part in it.
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

        return new Result<ParticipantView>(
            found.View.Status == HatStatus.Closed ? found.View : WithOnlyYourPick(found.View),
            HttpStatusCode.OK);
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
