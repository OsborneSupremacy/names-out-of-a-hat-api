namespace GiftExchange.Library.Utility;

/// <summary>
/// Boilerplate code for adapting an ApiGateway request and response to/from a business class
/// request/response.
/// </summary>
internal class ApiGatewayAdapter
{
    private readonly ILogger<ApiGatewayAdapter> _logger;

    private readonly JsonService _jsonService;

    private readonly IServiceProvider _serviceProvider;

    public ApiGatewayAdapter(
        ILogger<ApiGatewayAdapter> logger,
        JsonService jsonService,
        IServiceProvider serviceProvider
        )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonService = jsonService ?? throw new ArgumentNullException(nameof(jsonService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task<APIGatewayProxyResponse> AdaptAsync<TRequest, TResponse>(
        APIGatewayProxyRequest request,
        Func<TRequest, Task<Result<TResponse>>> handler
    )
    {
        var deserializedRequest = request.GetInnerRequest<TRequest>(_jsonService);

        if(deserializedRequest.IsFaulted)
            return ProxyResponseBuilder.Build(deserializedRequest.StatusCode, deserializedRequest.Exception.Message);

        var innerRequest = ApplyAuthenticatedCaller(deserializedRequest.Value, request);

        var (isValid, validationError) = GetValidationError(innerRequest);

        if(!isValid)
        {
            _logger.LogWarning("Validation failed for {RequestType}: {ValidationError}", typeof(TRequest).Name, validationError);
            return ProxyResponseBuilder.Build(HttpStatusCode.BadRequest, BuildSerializedErrorResponse(validationError));
        }

        var result = await handler(innerRequest);

        if(result.IsFaulted)
            return ProxyResponseBuilder
                .Build(result.StatusCode, BuildSerializedErrorResponse(result.Exception.Message));

        if(result.Value is StatusCodeOnlyResponse)
            return ProxyResponseBuilder.Build(result.StatusCode);

        return ProxyResponseBuilder
            .Build(result.StatusCode, _jsonService.SerializeDefault(result.Value));
    }

    public async Task<APIGatewayProxyResponse> AdaptAsync<TRequest, TResponse>(
        TRequest innerRequest,
        Func<TRequest, Task<Result<TResponse>>> handler
    )
    {
        var (isValid, validationError) = GetValidationError(innerRequest);

        if(!isValid)
        {
            _logger.LogWarning("Validation failed for {RequestType}: {ValidationError}", typeof(TRequest).Name, validationError);
            return ProxyResponseBuilder.Build(HttpStatusCode.BadRequest, BuildSerializedErrorResponse(validationError));
        }

        var result = await handler(innerRequest);

        if(result.IsFaulted)
            return ProxyResponseBuilder.Build(result.StatusCode, BuildSerializedErrorResponse(result.Exception.Message));

        if(result.Value is StatusCodeOnlyResponse)
            return ProxyResponseBuilder.Build(result.StatusCode);

        return ProxyResponseBuilder
            .Build(result.StatusCode, _jsonService.SerializeDefault(result.Value));
    }

    /// <summary>
    /// Replaces any client-supplied organizer or participant email with the one the authorizer
    /// established. The substitution happens here rather than in each service so that a new
    /// endpoint cannot forget to do it.
    /// </summary>
    /// <remarks>
    /// A request is scoped to one or the other, never both.
    /// </remarks>
    private static TRequest ApplyAuthenticatedCaller<TRequest>(
        TRequest innerRequest,
        APIGatewayProxyRequest request
    ) =>
        innerRequest switch
        {
            IOrganizerScopedRequest scopedRequest =>
                (TRequest)scopedRequest.WithOrganizerEmail(request.GetAuthenticatedEmail()),
            IParticipantScopedRequest scopedRequest =>
                (TRequest)scopedRequest.WithParticipantEmail(request.GetAuthenticatedEmail()),
            _ => innerRequest
        };

    private string BuildSerializedErrorResponse(string errorMessage)
    {
        var errorResponse = new ErrorResponse
        {
            Message = errorMessage
        };
        return _jsonService.SerializeDefault(errorResponse);
    }

    private (bool isValid, string error) GetValidationError<TRequest>(TRequest request)
    {
        var validator = _serviceProvider.GetService<IValidator<TRequest>>();

        if(validator is null)
            return (true, string.Empty);

        var validationResult = validator.Validate(request);

        if(validationResult.IsValid)
            return (true, string.Empty);

        return (false, validationResult.Errors.Select(e => e.ErrorMessage).Aggregate((a, b) => $"{a}; {b}"));
    }
}
