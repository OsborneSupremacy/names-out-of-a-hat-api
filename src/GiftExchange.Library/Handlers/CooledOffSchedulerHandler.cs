namespace GiftExchange.Library.Handlers;

[UsedImplicitly]
public class CooledOffSchedulerHandler
{
    private IServiceProvider? _serviceProvider;
    private readonly Lock _serviceProviderLock = new();

    public CooledOffSchedulerHandler() { }

    protected CooledOffSchedulerHandler(IServiceProvider serviceProvider)
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

    public async Task FunctionHandler(HatCooledOffScheduleRequest request, ILambdaContext context)
    {
        var giftExchangeProvider = GetServiceProvider().GetRequiredService<GiftExchangeProvider>();

        if (request.HatId == Guid.Empty || string.IsNullOrWhiteSpace(request.OrganizerEmail))
        {
            context.Logger.LogError("Scheduler payload was invalid. HatId: {HatId}; OrganizerEmail present: {HasOrganizerEmail}", request.HatId, !string.IsNullOrWhiteSpace(request.OrganizerEmail));
            return;
        }

        await giftExchangeProvider
            .TryTransitionHatToCooledOffAsync(request.OrganizerEmail, request.HatId);

        context.Logger.LogInformation("Scheduler transition attempted for hat {HatId}.", request.HatId);
    }
}
