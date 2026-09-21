namespace GiftExchange.Library.Services;

internal class GetHatService : IApiGatewayHandler
{
    private readonly ApiGatewayAdapter _adapter;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    public GetHatService(ApiGatewayAdapter adapter, GiftExchangeProvider giftExchangeProvider)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        var organizerEmail = request.GetAuthenticatedEmail();
        var hatId = request.GetIdPathParameter();

        return await _adapter
            .AdaptAsync(
                new GetHatRequest
                {
                    OrganizerEmail = organizerEmail,
                    HatId = hatId
                },
                GetHatAsync)
            .ConfigureAwait(false);
    }

    internal async Task<Result<Hat>> GetHatAsync(GetHatRequest request)
    {
        var (hatExists, hat) = await _giftExchangeProvider
            .GetHatAsync(request.OrganizerEmail, request.HatId)
            .ConfigureAwait(false);

        if(!hatExists)
            return new Result<Hat>(new KeyNotFoundException($"HatId {hat.Id} not found"), HttpStatusCode.NotFound);

        if(hat.Status != HatStatus.Closed)
            hat = RedactPickedRecipients(hat);

        return new Result<Hat>(hat, HttpStatusCode.OK);
    }

    private Hat RedactPickedRecipients(Hat hat) =>
        hat with
        {
            Participants = hat.Participants
                .Select(p => p with
                {
                    PickedRecipient = string.IsNullOrWhiteSpace(p.PickedRecipient.Email) ? Persons.Empty : Persons.Redacted
                })
                .ToImmutableList()
        };
}
