using AWS.Lambda.Powertools.Tracing;

namespace GiftExchange.Library.Handlers;

/// <summary>
/// Entry point for the daily sweep over exchange dates: close prompts, then the retention purge.
///
/// Invoked by one recurring EventBridge schedule rather than a schedule per exchange. See
/// <see cref="ExchangeDateSweepService"/> for why.
/// </summary>
[UsedImplicitly]
public class ExchangeDateSweepHandler
{
    private IServiceProvider? _serviceProvider;
    private readonly Lock _serviceProviderLock = new();

    public ExchangeDateSweepHandler() { }

    protected ExchangeDateSweepHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    private IServiceProvider GetServiceProvider()
    {
        if (_serviceProvider is not null) return _serviceProvider;
        using (_serviceProviderLock.EnterScope())
        {
            if (_serviceProvider is not null) return _serviceProvider;
            _serviceProvider = ServiceProviderBuilder.Build();
        }

        return _serviceProvider;
    }

    /// <remarks>
    /// Nothing is thrown out of here. A failed run is simply the same run tomorrow, and a scheduler
    /// retry within the hour buys nothing that waiting a day does not.
    /// </remarks>
    [Tracing(CaptureMode = TracingCaptureMode.Error)]
    public async Task FunctionHandler(ExchangeDateSweepRequest request, ILambdaContext context)
    {
        var service = GetServiceProvider().GetRequiredService<ExchangeDateSweepService>();

        try
        {
            var result = await service.ExecuteAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);

            context.Logger.LogInformation(
                $"Exchange date sweep finished. Close prompts sent: {result.ClosePromptsSent}; hats deleted: {result.HatsPurged}.");
        }
        catch (Exception exception)
        {
            context.Logger.LogError($"Exchange date sweep failed. Will run again tomorrow. Exception: {exception}");
        }
    }
}
