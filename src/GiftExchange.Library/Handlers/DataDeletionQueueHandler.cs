using Amazon.Lambda.SQSEvents;

using AWS.Lambda.Powertools.Tracing;

namespace GiftExchange.Library.Handlers;

public class DataDeletionQueueHandler
{
    private IServiceProvider? _serviceProvider;
    private readonly Lock _serviceProviderLock = new();

    public DataDeletionQueueHandler() { }

    protected DataDeletionQueueHandler(IServiceProvider serviceProvider)
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

    [UsedImplicitly]
    // The far end of the trace DataDeletionQueue propagates. Error capture only, for the reason
    // InvitationQueueHandler gives: an exception here is raised while holding somebody's address.
    [Tracing(CaptureMode = TracingCaptureMode.Error)]
    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        var service = GetServiceProvider().GetRequiredService<DataDeletionQueueHandlerService>();

        foreach (var record in sqsEvent.Records)
            await service.ProcessRecordAsync(record, context)
                .ConfigureAwait(false);
    }
}
