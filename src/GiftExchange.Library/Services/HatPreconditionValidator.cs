namespace GiftExchange.Library.Services;

internal class HatPreconditionValidator
{
    private readonly ILogger<HatPreconditionValidator> _logger;

    private readonly IContentModerationService _contentModerationService;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    public HatPreconditionValidator(
        ILogger<HatPreconditionValidator> logger,
        GiftExchangeProvider giftExchangeProvider,
        IContentModerationService contentModerationService
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _contentModerationService = contentModerationService ??
                                    throw new ArgumentNullException(nameof(contentModerationService));
    }

    /// <remarks>
    /// Moderation comes last, after the exchange has been found and its status checked. It used to
    /// come first, which meant any signed-in caller could spend a Comprehend call on an exchange id
    /// they made up, and be told "not found" only afterwards. Now only the organizer of a real
    /// exchange that can take the change gets that far, and only for text that changed.
    /// </remarks>
    public async Task<HatPreconditionResponse> ValidateAsync(HatPreconditionRequest request)
    {
        var (hatExists, hat) = await _giftExchangeProvider
            .GetHatAsync(request.OrganizerEmail, request.HatId)
            .ConfigureAwait(false);

        if (!hatExists)
            return new HatPreconditionResponse
            {
                PreconditionsMet = false,
                PreconditionFailureMessage = new PreconditionFailureMessage
                {
                    StatusCode = HttpStatusCode.NotFound,
                    FailureMessage = $"Hat with id {request.HatId} not found"
                },
                Hat = Hats.Empty
            };

        if (!request.ValidHatStatuses.Contains(hat.Status))
        {
            _logger.LogError("Hat status {HatStatus} is not valid for this operation. Valid statuses are {ValidStatuses}", hat.Status, string.Join(',', request.ValidHatStatuses));
            return new HatPreconditionResponse
            {
                PreconditionsMet = false,
                PreconditionFailureMessage = new PreconditionFailureMessage
                {
                    StatusCode = HttpStatusCode.Conflict,
                    FailureMessage = $"Hat status {hat.Status} is not valid for this operation"
                },
                Hat = Hats.Empty
            };
        }

        var moderationResponse = await ModerateAsync(ChangedFields(request, hat))
            .ConfigureAwait(false);

        if (!moderationResponse.PreconditionsMet)
            return moderationResponse;

        return new HatPreconditionResponse
        {
            PreconditionsMet = true,
            PreconditionFailureMessage = PreconditionFailureMessages.Empty,
            Hat = hat
        };
    }

    private static Dictionary<string, string> ChangedFields(HatPreconditionRequest request, Hat hat)
    {
        var stored = request.StoredValues(hat);

        return request.FieldsToModerate
            .Where(field => !stored.TryGetValue(field.Key, out var current) || current != field.Value)
            .ToDictionary(field => field.Key, field => field.Value);
    }

    private async Task<HatPreconditionResponse> ModerateAsync(Dictionary<string, string> fieldsToModerate)
    {
        if (fieldsToModerate.Count == 0)
            return new HatPreconditionResponse
            {
                PreconditionsMet = true,
                PreconditionFailureMessage = PreconditionFailureMessages.Empty,
                Hat = Hats.Empty
            };

        var (isAcceptable, errorMessage) = await _contentModerationService
            .ValidateMultipleFieldsAsync(fieldsToModerate)
            .ConfigureAwait(false);

        if (!isAcceptable)
            return (new HatPreconditionResponse
            {
                PreconditionsMet = false,
                PreconditionFailureMessage = new PreconditionFailureMessage
                {
                    StatusCode = HttpStatusCode.BadRequest, FailureMessage = string.Join(';', errorMessage)
                },
                Hat = Hats.Empty
            });

        return new HatPreconditionResponse
        {
            PreconditionsMet = true,
            PreconditionFailureMessage = PreconditionFailureMessages.Empty,
            Hat = Hats.Empty
        };
    }
}



