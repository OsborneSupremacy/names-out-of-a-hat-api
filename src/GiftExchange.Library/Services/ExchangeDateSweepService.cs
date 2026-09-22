namespace GiftExchange.Library.Services;

/// <summary>
/// Once a day, does the two things that are measured from an exchange's date: asks organizers to
/// close exchanges that have happened, and deletes exchanges that happened long enough ago.
/// </summary>
/// <remarks>
/// A daily sweep rather than a schedule per exchange, which is how the cool-off and the delivery
/// check are done. Those two are fixed offsets from a moment that never moves -- the send. This is
/// measured from a date the organizer can change, and a schedule per exchange would have to be
/// found and moved every time they did. A sweep reads the date as it stands.
///
/// Each exchange is handled on its own and a failure on one is logged and passed over. The sweep
/// runs again tomorrow, and everything it does is safe to find half-done: a prompt is claimed
/// before it is sent, and a purge is one transaction per exchange.
/// </remarks>
[UsedImplicitly]
internal class ExchangeDateSweepService
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly ClosePromptEmailCompositionService _emailCompositionService;

    private readonly AutomaticEmailSender _emailSender;

    private readonly ILogger<ExchangeDateSweepService> _logger;

    public ExchangeDateSweepService(
        GiftExchangeProvider giftExchangeProvider,
        ClosePromptEmailCompositionService emailCompositionService,
        AutomaticEmailSender emailSender,
        ILogger<ExchangeDateSweepService> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _emailCompositionService = emailCompositionService ?? throw new ArgumentNullException(nameof(emailCompositionService));
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <param name="now">The moment the sweep counts from. A parameter so tests can move it.</param>
    internal async Task<ExchangeDateSweepResponse> ExecuteAsync(DateTimeOffset now)
    {
        var closePromptsSent = await SendClosePromptsAsync(now).ConfigureAwait(false);
        var hatsPurged = await PurgeAsync(now).ConfigureAwait(false);

        return new ExchangeDateSweepResponse { ClosePromptsSent = closePromptsSent, HatsPurged = hatsPurged };
    }

    private async Task<int> SendClosePromptsAsync(DateTimeOffset now)
    {
        var candidates = await _giftExchangeProvider
            .ListHatsDueClosePromptAsync(ExchangeDates.ClosePromptCutoff(now))
            .ConfigureAwait(false);

        var sent = 0;

        foreach (var candidate in candidates)
        {
            try
            {
                if (await SendClosePromptAsync(candidate, now).ConfigureAwait(false))
                    sent++;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to send the close prompt for hat {HatId}.", candidate.HatId);
            }
        }

        return sent;
    }

    private async Task<bool> SendClosePromptAsync(ExchangeDateSweepCandidate candidate, DateTimeOffset now)
    {
        var (exists, hat) = await _giftExchangeProvider
            .GetHatAsync(candidate.OrganizerEmail, candidate.HatId)
            .ConfigureAwait(false);

        // Deleted or closed since the list was read. Nothing to say.
        if (!exists || hat.Status == HatStatus.Closed)
            return false;

        // Another sweep got here first. Only possible if two overlap, which a daily schedule should
        // never do, but a claim that is checked costs one row.
        if (!await _giftExchangeProvider.TryClaimClosePromptAsync(hat.Id, now).ConfigureAwait(false))
            return false;

        await _emailSender
            .SendAsync(
                hat.Organizer.Email,
                ClosePromptEmailCompositionService.GetSubject(hat),
                _emailCompositionService.ComposeEmail(hat))
            .ConfigureAwait(false);

        _logger.LogInformation("Asked the organizer of hat {HatId} to close it.", hat.Id);

        return true;
    }

    private async Task<int> PurgeAsync(DateTimeOffset now)
    {
        var cutoff = ExchangeDates.PurgeCutoff(now);

        var hatIds = await _giftExchangeProvider
            .ListHatsDuePurgeAsync(cutoff)
            .ConfigureAwait(false);

        var purged = 0;

        foreach (var hatId in hatIds)
        {
            try
            {
                if (await _giftExchangeProvider.PurgeHatAsync(hatId, cutoff).ConfigureAwait(false))
                {
                    purged++;
                    _logger.LogInformation("Deleted hat {HatId}: its date is more than {RetentionMonths} months ago.", hatId, ExchangeDates.RetentionMonths);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to delete hat {HatId} past its retention date.", hatId);
            }
        }

        return purged;
    }
}
