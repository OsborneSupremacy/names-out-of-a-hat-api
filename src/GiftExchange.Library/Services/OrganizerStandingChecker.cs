namespace GiftExchange.Library.Services;

/// <summary>
/// Whether an organizer may send mail to participants, judged by how many of the people they have
/// sent to lately have said they did not want it.
/// </summary>
/// <remarks>
/// This is the check that stands in front of the send path, where <c>HatCreationLimiter</c> stands
/// in front of creation. What it protects is less any one recipient — each of them is already
/// protected by the do-not-add lists, which stop an organizer adding somebody who has refused —
/// than the sending account every organizer shares. SES reviews an account at a complaint rate of
/// 0.1% and may pause it at 0.5%, and one organizer mailing strangers can get there on their own.
///
/// Two signals, both of which survive the deletion of the exchanges that caused them (see
/// <c>GiftExchangeProvider.CountOrganizerRefusalsAsync</c> for what is left out and why):
///
/// A spam complaint is the strongest thing a recipient can say and costs the account directly, so
/// it has a threshold of its own. Asking never to be added by this organizer again is weaker — it
/// can be one family's argument — so it only counts alongside complaints, towards a higher one.
///
/// The thresholds are absolute rather than rates, which looks like the less careful choice and is
/// not. A rate needs a denominator, and the only record of how many people an organizer has mailed
/// is the delivery rows, which go with the exchange. And the ceiling on a legitimate organizer's
/// reach is already set elsewhere: five open exchanges of fifty is two hundred and fifty people,
/// against which three complaints is over one percent — twice the rate at which SES pauses the
/// whole account.
///
/// Below the refusal there is a warning, logged and nothing else. It is the line a person should
/// read before the organizer is stopped, which is when a mistake can still be caught cheaply. The
/// log message is fixed text so a metric filter can count it.
///
/// Not permanent. The window rolls, and an organizer whose complaints age out of it may send again
/// without anybody deciding they should. That is deliberate — a family organizer caught by three
/// cousins in one bad December should not be locked out for good — and it is also the one thing to
/// revisit if the same organizers keep coming back.
///
/// Fails the way <c>DoNotAddService</c> does, by letting the exception through to become a 500.
/// Failing open would send mail on behalf of the one organizer this exists to stop; failing closed
/// into a refusal would tell every organizer they had been reported, during an outage, which is
/// false.
/// </remarks>
internal class OrganizerStandingChecker
{
    /// <summary>Distinct complainants inside <see cref="Window"/> that stop an organizer sending.</summary>
    internal const int SuspendAtComplaints = 3;

    /// <summary>
    /// Distinct people inside <see cref="Window"/> who complained or asked not to be added by this
    /// organizer again, at which the organizer is stopped even with fewer complaints.
    /// </summary>
    internal const int SuspendAtRefusals = 5;

    /// <summary>Refusals inside <see cref="Window"/> at which the organizer is logged for review.</summary>
    internal const int ReviewAtRefusals = 2;

    /// <summary>
    /// How far back complaints and refusals count. A season, roughly: long enough that an organizer
    /// cannot wait out a suspension and send again the same December, short enough that last
    /// year's does not follow them into this one.
    /// </summary>
    internal static readonly TimeSpan Window = TimeSpan.FromDays(90);

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly ILogger<OrganizerStandingChecker> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public OrganizerStandingChecker(GiftExchangeProvider giftExchangeProvider, ILogger<OrganizerStandingChecker> logger)
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Whether this organizer may send mail to participants right now.</summary>
    /// <remarks>
    /// The counts are logged and the organizer's address is not. Finding which organizer a warning
    /// is about is a query against the two tables, done by somebody who has decided to look.
    /// </remarks>
    public async Task<OrganizerStandingResponse> CheckAsync(string organizerEmail)
    {
        var refusals = await _giftExchangeProvider
            .CountOrganizerRefusalsAsync(new CountOrganizerRefusalsRequest
            {
                OrganizerEmail = organizerEmail,
                Since = DateTimeOffset.UtcNow.Subtract(Window)
            })
            .ConfigureAwait(false);

        if (refusals.Complaints >= SuspendAtComplaints || refusals.Refusals >= SuspendAtRefusals)
        {
            _logger.LogWarning(
                "Organizer sending suspended: {Complaints} complaints and {Refusals} refusals in the last {WindowDays} days.",
                refusals.Complaints,
                refusals.Refusals,
                Window.TotalDays);

            return new OrganizerStandingResponse
            {
                MaySend = false,
                RefusalMessage = RefusalMessage,
                RefusalStatusCode = HttpStatusCode.Forbidden
            };
        }

        if (refusals.Complaints > 0 || refusals.Refusals >= ReviewAtRefusals)
            _logger.LogWarning(
                "Organizer sending under review: {Complaints} complaints and {Refusals} refusals in the last {WindowDays} days.",
                refusals.Complaints,
                refusals.Refusals,
                Window.TotalDays);

        return new OrganizerStandingResponse
        {
            MaySend = true,
            RefusalMessage = string.Empty,
            RefusalStatusCode = HttpStatusCode.OK
        };
    }

    /// <summary>
    /// What a stopped organizer is told.
    /// </summary>
    /// <remarks>
    /// Says what happened in terms of what recipients did, as <c>DoNotAddService.RefusalMessage</c>
    /// does, because that is the truth and because it is the one explanation that does not read as
    /// a bug. Names nobody, and does not need to: the organizer can already see which of their
    /// participants complained, in the delivery column of their own exchange.
    ///
    /// Says it ends, without saying when. The date is when the oldest report leaves the window,
    /// which is a fact about who reported them and when — more than an organizer needs to know,
    /// and exactly what one planning to wait it out would want.
    /// </remarks>
    internal const string RefusalMessage =
        "Sending email from your account is paused, because several people who received email from your gift exchanges "
        + "reported it as spam or asked not to hear from you again. The pause lifts on its own after a while. "
        + "If you think this is a mistake, let us know through the feedback form.";
}
