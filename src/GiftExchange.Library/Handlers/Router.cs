using Amazon.Lambda.Serialization.SystemTextJson;
using AWS.Lambda.Powertools.Tracing;
using AWS.Lambda.Powertools.Metrics;

[assembly: LambdaSerializer(typeof(SourceGeneratorLambdaJsonSerializer<GiftExchangeJsonSerializerContext>))]

namespace GiftExchange.Library.Handlers;

[UsedImplicitly]
public class Router
{
    private IServiceProvider? _serviceProvider;
    private readonly Lock _serviceProviderLock = new();

    // Read once per container. The settings only change with a deployment, which starts new ones.
    private readonly OriginGuard _originGuard = OriginGuard.FromEnvironment();

    public Router() { }

    protected Router(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    private IServiceProvider GetServiceProvider()
    {
        if(_serviceProvider is not null) return _serviceProvider;
        using (_serviceProviderLock.EnterScope())
        {
            if(_serviceProvider is not null) return _serviceProvider;
            _serviceProvider = ServiceProviderBuilder.Build();
        }
        return _serviceProvider;
    }

    [Metrics(CaptureColdStart = true, Namespace = nameof(Router))]
    [Tracing]
    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    )
    {
        var serviceKey = $"{request.HttpMethod}{request.Resource}".ToLowerInvariant();

        // Before anything else, and before the service provider is built: a request that went
        // around CloudFront has not been past the rate limits, and nothing it asks for should cost
        // a database connection.
        switch (_originGuard.Check(request.Headers))
        {
            case OriginVerdict.Refuse:
                context.Logger.LogWarning($"Refused a request to {serviceKey} that did not come through CloudFront.");
                return ProxyResponseBuilder.Build(HttpStatusCode.Forbidden);
            case OriginVerdict.AllowButReport:
                context.Logger.LogWarning($"A request to {serviceKey} did not come through CloudFront; allowed because the origin check is not enforced yet.");
                break;
        }

        // Read before the provider is built, because building it is what stops it being one.
        var isColdStart = _serviceProvider is null;

        var service = GetServiceProvider()
            .GetKeyedService<IApiGatewayHandler>(serviceKey);

        Tracing.AddAnnotation("route", serviceKey);

        if (service is not null)
            return await service.FunctionHandler(request, context);

        context.Logger.LogError($"Couldn't find api gateway handler for {serviceKey}");
        return ProxyResponseBuilder.Build(HttpStatusCode.InternalServerError);
    }
}
