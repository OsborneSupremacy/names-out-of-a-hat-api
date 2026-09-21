using System.Globalization;

namespace GiftExchange.Library.Services;

internal class GetHatsService : IApiGatewayHandler
{
    internal const int PageSize = 5;

    private readonly ApiGatewayAdapter _adapter;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    public GetHatsService(
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
        return _adapter.AdaptAsync(new GetHatsRequest
        {
            OrganizerEmail = request.GetAuthenticatedEmail(),
            Page = ReadPage(request)
        }, ExecuteAsync);
    }

    /// <summary>
    /// Absent means the first page. Anything that is not a number becomes 0, which the validator
    /// turns away, so a malformed link is told so rather than silently shown page 1.
    /// </summary>
    private static int ReadPage(APIGatewayProxyRequest request)
    {
        if (request.QueryStringParameters is null
            || !request.QueryStringParameters.TryGetValue("page", out var page)
            || string.IsNullOrWhiteSpace(page))
            return 1;

        return int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    internal async Task<Result<GetHatsResponse>> ExecuteAsync(GetHatsRequest request)
    {
        var page = await _giftExchangeProvider
            .GetHatsAsync(new GetHatsPageRequest
            {
                OrganizerEmail = request.OrganizerEmail,
                Page = request.Page,
                PageSize = PageSize
            })
            .ConfigureAwait(false);

        return new Result<GetHatsResponse>(new GetHatsResponse
        {
            OrganizerName = page.OrganizerName,
            Hats = page.Hats,
            Page = request.Page,
            PageSize = PageSize,
            TotalCount = page.TotalCount
        }, HttpStatusCode.OK);
    }
}
