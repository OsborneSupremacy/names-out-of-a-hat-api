using System.Globalization;

namespace GiftExchange.Library.Services;

/// <summary>
/// The gift exchanges the caller has been invited to, as opposed to the ones they organized.
/// </summary>
internal class GetParticipatingHatsService : IApiGatewayHandler
{
    internal const int PageSize = GetHatsService.PageSize;

    private readonly ApiGatewayAdapter _adapter;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    public GetParticipatingHatsService(
        ApiGatewayAdapter adapter,
        GiftExchangeProvider giftExchangeProvider
        )
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    )
    {
        return _adapter.AdaptAsync(new GetParticipatingHatsRequest
        {
            ParticipantEmail = request.GetAuthenticatedEmail(),
            Page = ReadPage(request)
        }, ExecuteAsync);
    }

    /// <summary>The same reading of <c>page</c> as <see cref="GetHatsService"/>.</summary>
    private static int ReadPage(APIGatewayProxyRequest request)
    {
        if (request.QueryStringParameters is null
            || !request.QueryStringParameters.TryGetValue("page", out var page)
            || string.IsNullOrWhiteSpace(page))
            return 1;

        return int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    internal async Task<Result<GetParticipatingHatsResponse>> ExecuteAsync(GetParticipatingHatsRequest request)
    {
        var page = await _giftExchangeProvider
            .GetParticipatingHatsAsync(new GetParticipatingHatsPageRequest
            {
                ParticipantEmail = request.ParticipantEmail,
                Page = request.Page,
                PageSize = PageSize
            })
            .ConfigureAwait(false);

        return new Result<GetParticipatingHatsResponse>(new GetParticipatingHatsResponse
        {
            Hats = page.Hats,
            Page = request.Page,
            PageSize = PageSize,
            TotalCount = page.TotalCount
        }, HttpStatusCode.OK);
    }
}
