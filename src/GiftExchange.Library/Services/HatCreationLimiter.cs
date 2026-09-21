namespace GiftExchange.Library.Services;

/// <summary>
/// Caps on how many gift exchanges one organizer can start in a day, and on how many they can have
/// open at once, shared by the two paths that start one: creating from scratch and copying a
/// finished exchange.
/// </summary>
/// <remarks>
/// A copy counts against both, the same as a fresh exchange. It has to: it writes a hat and a full
/// set of participants, so exempting it would leave the limits with a door next to them.
///
/// Worth being honest about what these are. Creating an exchange sends no mail and needs a
/// signed-in organizer, so neither is the thing standing between the application and a spam run —
/// the send path is. What they bound is how much one account can pile into the database, whether
/// by a script or by a stuck client retrying, and they do that at a height no legitimate organizer
/// should ever reach: five is more exchanges than a person runs in a season, let alone in a day or
/// at once.
///
/// The two are not redundant. The open limit on its own could be walked around by closing as it
/// went, and the cool-off before closing is minutes, not days. The daily limit on its own lets an
/// account accumulate five a day for as long as it cares to. Between them, what one organizer
/// holds open is bounded and so is the rate they turn it over.
///
/// The daily window rolls rather than resetting at midnight, which avoids having to decide whose
/// midnight it is. The cost is that "try again tomorrow" would be a lie, so the refusal names a
/// time instead.
/// </remarks>
internal class HatCreationLimiter
{
    /// <summary>Exchanges one organizer may start inside <see cref="Window"/>.</summary>
    internal const int DailyLimit = 5;

    /// <summary>Exchanges one organizer may have that are not yet <c>CLOSED</c>.</summary>
    internal const int OpenLimit = 5;

    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly ILogger<HatCreationLimiter> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public HatCreationLimiter(GiftExchangeProvider giftExchangeProvider, ILogger<HatCreationLimiter> logger)
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Whether this organizer may start another exchange right now.</summary>
    /// <remarks>
    /// The open limit is checked first. An organizer over both is better told the one that waiting
    /// will not fix, rather than being sent away until a time when they will only be refused again.
    ///
    /// Two requests arriving together can both read the same counts and both be allowed, so the
    /// real ceiling is the limit plus however many are in flight. That is left alone deliberately:
    /// the alternative is a reservation to serialize creation, and the difference between five and
    /// six does not justify it when the point is to stop hundreds.
    /// </remarks>
    public async Task<HatCreationLimitResponse> CheckAsync(string organizerEmail)
    {
        var open = await _giftExchangeProvider
            .CountOpenHatsAsync(organizerEmail)
            .ConfigureAwait(false);

        if (open >= OpenLimit)
        {
            _logger.LogWarning(
                "An organizer already has {OpenLimit} open gift exchanges; refusing another.",
                OpenLimit);

            return Refused(OpenRefusalMessage, HttpStatusCode.Conflict);
        }

        var created = await _giftExchangeProvider
            .CountHatsCreatedSinceAsync(new CountHatsCreatedSinceRequest
            {
                OrganizerEmail = organizerEmail,
                Since = DateTimeOffset.UtcNow.Subtract(Window)
            })
            .ConfigureAwait(false);

        if (created.Count < DailyLimit)
            return new HatCreationLimitResponse
            {
                WithinLimit = true,
                RefusalMessage = string.Empty,
                RefusalStatusCode = HttpStatusCode.OK
            };

        _logger.LogWarning(
            "An organizer has reached the daily limit of {DailyLimit} gift exchanges; refusing another.",
            DailyLimit);

        // The oldest one inside the window is the one that leaves it first, so its creation time
        // plus the window is the moment an allowance opens up again.
        return Refused(
            DailyRefusalMessage(created.EarliestCreatedAt.Add(Window)),
            HttpStatusCode.TooManyRequests);
    }

    private static HatCreationLimitResponse Refused(string message, HttpStatusCode statusCode) =>
        new()
        {
            WithinLimit = false,
            RefusalMessage = message,
            RefusalStatusCode = statusCode
        };

    /// <summary>
    /// Names the two ways to make room. Closing only works for an exchange that has cooled off, so
    /// deleting is offered alongside it for the ones that never got that far.
    /// </summary>
    internal static readonly string OpenRefusalMessage =
        $"You already have {OpenLimit} gift exchanges that aren't closed, which is as many as this application allows at once. "
        + "Close one that has finished, or delete one you no longer need, to start another.";

    /// <summary>
    /// Names when the organizer may try again, and the one thing they can do about it sooner.
    /// </summary>
    /// <remarks>
    /// Deleting is offered because the count is taken from the exchanges they own, so it genuinely
    /// works — see <c>GiftExchangeProvider.CountHatsCreatedSinceAsync</c>. Somebody who hit the
    /// limit making the same exchange five times over should not have to wait a day to fix it.
    /// </remarks>
    internal static string DailyRefusalMessage(DateTimeOffset nextAllowedAt) =>
        $"You have started {DailyLimit} gift exchanges in the past day, which is as many as this application allows. "
        + $"You can start another after {nextAllowedAt:HH:mm} UTC, or sooner if you delete one you no longer need.";
}
