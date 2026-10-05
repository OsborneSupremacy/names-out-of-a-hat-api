namespace GiftExchange.Library.Services;

/// <summary>
/// Leaving a gift exchange from the participant's own signed-in page.
/// </summary>
/// <remarks>
/// Does exactly what the leave link in an invitation does — the work is
/// <see cref="GiftExchangeLeaving"/>'s — with the participant found by their session rather than by
/// a token. The same people may do it: everybody but the organizer, whom
/// <see cref="GiftExchangeProvider.FindLeaveRouteForParticipantAsync"/> excludes by name since there
/// is no missing token here to exclude them by.
///
/// An organizer asking is answered as if the exchange did not exist, as on the email path. The page
/// never offers them the button, so the only way to ask is by hand.
/// </remarks>
internal class ParticipantLeaveService : IApiGatewayHandler
{
    private readonly ApiGatewayAdapter _adapter;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly GiftExchangeLeaving _leaving;

    public ParticipantLeaveService(
        ApiGatewayAdapter adapter,
        GiftExchangeProvider giftExchangeProvider,
        GiftExchangeLeaving leaving
    )
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _leaving = leaving ?? throw new ArgumentNullException(nameof(leaving));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<LeaveGiftExchangeRequest, StatusCodeOnlyResponse>(request, LeaveAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> LeaveAsync(LeaveGiftExchangeRequest request)
    {
        var (found, route) = await _giftExchangeProvider
            .FindLeaveRouteForParticipantAsync(new FindParticipantRouteRequest
            {
                ParticipantEmail = request.ParticipantEmail,
                HatId = request.HatId
            })
            .ConfigureAwait(false);

        var left = found
                   && await _leaving
                       .LeaveAsync(route, request.BlockOrganizer, request.BlockAnywhere)
                       .ConfigureAwait(false);

        return left
            ? new Result<StatusCodeOnlyResponse>(
                new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.NoContent },
                HttpStatusCode.NoContent)
            : new Result<StatusCodeOnlyResponse>(
                new KeyNotFoundException("Gift exchange not found."),
                HttpStatusCode.NotFound);
    }
}
