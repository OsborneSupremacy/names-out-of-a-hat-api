namespace GiftExchange.Library.Services;

/// <summary>
/// Caps how many distinct people one organizer can mail in a day and in a week, counted from a
/// ledger that outlives the exchanges the mail was sent from.
/// </summary>
/// <remarks>
/// This is the limit that stops create, send, delete, repeat. <c>HatCreationLimiter</c> cannot:
/// its counts come from the exchanges an organizer currently owns, so that deleting a mistake
/// gives the slot back, and the same property lets somebody cycle through exchanges without ever
/// holding five. Counting sends rather than exchanges, and keeping the count when the exchange
/// goes, is what bounds how many people one account can reach.
///
/// Distinct addresses rather than messages, which is what makes it fair. Copying last year's
/// exchange and sending it again to the same family costs nothing new; correcting an address and
/// resending costs one. Mailing a fresh list of fifty every hour costs fifty every hour, and runs
/// out before lunch.
///
/// The numbers are set against the reach <c>OrganizerStandingChecker</c> already assumes: five
/// open exchanges of fifty is two hundred and fifty people, which is the week. A day of a hundred
/// is two office-sized exchanges, or an office and every family an organizer could plausibly run.
///
/// A send is allowed or refused whole. Mailing half an exchange would leave the other half unable
/// to find out who they drew, and the draw cannot be redone without resetting it.
///
/// Two sends arriving together can both read the same count and both be allowed, which is left
/// alone for the reason <c>HatCreationLimiter</c> gives: the point is to stop thousands, and
/// serializing sends is not worth telling a hundred from a hundred and fifty.
/// </remarks>
internal class OrganizerSendLimiter
{
    /// <summary>Distinct people one organizer may mail inside <see cref="DailyWindow"/>.</summary>
    internal const int DailyLimit = 100;

    /// <summary>Distinct people one organizer may mail inside <see cref="WeeklyWindow"/>.</summary>
    internal const int WeeklyLimit = 250;

    internal static readonly TimeSpan DailyWindow = TimeSpan.FromHours(24);

    internal static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly ILogger<OrganizerSendLimiter> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public OrganizerSendLimiter(GiftExchangeProvider giftExchangeProvider, ILogger<OrganizerSendLimiter> logger)
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Whether this organizer may mail these people right now.</summary>
    /// <remarks>
    /// The week is checked first, for the reason <c>HatCreationLimiter</c> checks the open limit
    /// first: an organizer over both is better told the later time, rather than being sent away
    /// until a moment when they will only be refused again.
    /// </remarks>
    public async Task<OrganizerSendLimitResponse> CheckAsync(CheckOrganizerSendLimitRequest request)
    {
        var organizerEmail = request.OrganizerEmail.ToMailboxKey();

        var now = DateTimeOffset.UtcNow;

        var mailed = await _giftExchangeProvider
            .ListOrganizerRecipientsAsync(new ListOrganizerRecipientsRequest
            {
                OrganizerEmail = request.OrganizerEmail,
                Since = now.Subtract(WeeklyWindow)
            })
            .ConfigureAwait(false);

        var recipients = request.RecipientEmails
            .Select(email => email.ToNormalizedEmail())
            .Where(email => !string.IsNullOrWhiteSpace(email) && email.ToMailboxKey() != organizerEmail)
            .Distinct()
            .ToImmutableList();

        var weekly = Check(mailed.LastSentAt, recipients, now, WeeklyWindow, WeeklyLimit);

        if (weekly is { } weeklyRefusal)
            return Refused(weeklyRefusal, WeeklyLimit, "week");

        var daily = Check(mailed.LastSentAt, recipients, now, DailyWindow, DailyLimit);

        if (daily is { } dailyRefusal)
            return Refused(dailyRefusal, DailyLimit, "day");

        return new OrganizerSendLimitResponse
        {
            WithinLimit = true,
            RefusalMessage = string.Empty,
            RefusalStatusCode = HttpStatusCode.OK
        };
    }

    /// <summary>
    /// When this send could go inside one window, or null if it can go now.
    /// </summary>
    /// <remarks>
    /// Only addresses not already mailed inside the window are new reach. Those already mailed
    /// cost nothing, which is what lets an organizer resend to the same people.
    ///
    /// The time returned is when enough of the people already counted have aged out to make room:
    /// each leaves the window when their last send does, so sorted by that, the one that makes
    /// just enough room is the <c>excess</c>-th. If the send is bigger than the limit on its own,
    /// no amount of waiting helps, and the end of the window is the honest answer — nothing in the
    /// application can produce that today, since an exchange holds fifty.
    /// </remarks>
    private static DateTimeOffset? Check(
        ImmutableDictionary<string, DateTimeOffset> lastSentAt,
        ImmutableList<string> recipients,
        DateTimeOffset now,
        TimeSpan window,
        int limit
    )
    {
        var since = now.Subtract(window);

        var inWindow = lastSentAt
            .Where(pair => pair.Value >= since)
            .ToImmutableDictionary();

        var added = recipients.Count(email => !inWindow.ContainsKey(email));

        var excess = inWindow.Count + added - limit;

        if (excess <= 0)
            return null;

        // Somebody in this send who was already mailed is mailed again now, so they do not leave.
        var leaving = inWindow
            .Where(pair => !recipients.Contains(pair.Key))
            .Select(pair => pair.Value)
            .Order()
            .ToList();

        return excess <= leaving.Count
            ? leaving[excess - 1].Add(window)
            : now.Add(window);
    }

    private OrganizerSendLimitResponse Refused(DateTimeOffset nextAllowedAt, int limit, string period)
    {
        _logger.LogWarning(
            "An organizer has reached the limit of {Limit} people emailed in a {Period}; refusing the send.",
            limit,
            period);

        return new OrganizerSendLimitResponse
        {
            WithinLimit = false,
            RefusalMessage = RefusalMessage(limit, period, nextAllowedAt),
            RefusalStatusCode = HttpStatusCode.TooManyRequests
        };
    }

    /// <summary>
    /// Names the limit, when the send can go, and that the people already mailed are not the
    /// problem.
    /// </summary>
    /// <remarks>
    /// Says nothing about deleting, unlike <c>HatCreationLimiter.DailyRefusalMessage</c>, because
    /// deleting does not help here and suggesting it would send the organizer off to destroy an
    /// exchange for nothing.
    /// </remarks>
    internal static string RefusalMessage(int limit, string period, DateTimeOffset nextAllowedAt) =>
        $"Your gift exchanges have emailed {limit} different people in the past {period}, which is as many as this application allows. "
        + $"You can send these invitations after {nextAllowedAt:yyyy-MM-dd HH:mm} UTC.";
}
