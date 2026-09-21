namespace GiftExchange.Library.Services;

/// <summary>
/// Changes the display name the caller is known by.
/// </summary>
/// <remarks>
/// The same write <see cref="EditParticipantNameService"/> makes, through the same provider method,
/// and the difference between the two is only whose address is being renamed. It used to be its own
/// implementation, and the two drifted in the way two spellings of one fact do; one write is how
/// they stay the same.
///
/// Nothing here can be refused for want of standing. A person may always change their own name,
/// which is the first of the two rules <c>PersonEntity.AddedByPersonId</c> exists to express, so
/// the Forbidden the other endpoint can return is unreachable from this one.
/// </remarks>
[UsedImplicitly]
internal class UpdateProfileService : IApiGatewayHandler
{
    private readonly ApiGatewayAdapter _adapter;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly IContentModerationService _contentModerationService;

    public UpdateProfileService(
        ApiGatewayAdapter adapter,
        GiftExchangeProvider giftExchangeProvider,
        IContentModerationService contentModerationService
    )
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _contentModerationService = contentModerationService ?? throw new ArgumentNullException(nameof(contentModerationService));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context) =>
        _adapter.AdaptAsync<UpdateProfileRequest, StatusCodeOnlyResponse>(request, ExecuteAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> ExecuteAsync(UpdateProfileRequest request)
    {
        var (isAcceptable, moderationErrors) = await _contentModerationService
            .ValidateMultipleFieldsAsync(new Dictionary<string, string> { ["name"] = request.Name })
            .ConfigureAwait(false);

        if (!isAcceptable)
            return new Result<StatusCodeOnlyResponse>(
                new InvalidOperationException(string.Join(" ", moderationErrors)),
                HttpStatusCode.BadRequest);

        // Nothing a person renaming themselves can be refused for: the name is not checked against
        // anybody else's, and a person may always change their own.
        await _giftExchangeProvider
            .RenamePersonAsync(new RenamePersonRequest
            {
                Email = request.OrganizerEmail,
                Name = request.Name,
                RequestedByEmail = request.OrganizerEmail
            })
            .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(
            new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.OK },
            HttpStatusCode.OK);
    }
}
