using System.Globalization;
using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// The box at the bottom of a follow-up email that tells its reader they were sent an invitation, and
/// how to find it.
/// </summary>
/// <remarks>
/// Gmail files some invitations under Promotions — a logo and three coloured buttons look like
/// marketing — while the short, plain follow-ups that come later land in the inbox. So the first
/// thing some participants read is somebody asking what they'd like for a gift exchange they have
/// never heard of. This is for them.
///
/// Shown until they have plainly read the invitation, which is the one thing a mail client never
/// tells a sender honestly. Open tracking was rejected for that reason (see the delivery tracking
/// notes in the README); what is used instead is a button press on a page that only the invitation,
/// or the copy of it on the site, could have led them to. A mail scanner fetches links but does not
/// press buttons, so the signal is clean in the direction that matters: nobody who has not seen
/// their invitation is ever taken to have seen it.
///
/// The search hint names the subject line rather than the date. The date is approximate — it is
/// written in UTC, and an invitation sent on an American evening is dated the next day there — but
/// the subject is exact, and typing it into a mail client's search finds the message whichever
/// folder it was filed in. The sender is given as the product alone, because invitations sent
/// before the From line named the organizer came from the bare address.
/// </remarks>
[UsedImplicitly]
internal class InvitationReminderService
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly ILogger<InvitationReminderService> _logger;

    public InvitationReminderService(GiftExchangeProvider giftExchangeProvider, ILogger<InvitationReminderService> logger)
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The box for an email to this participant, or empty when they don't need one.
    /// </summary>
    /// <remarks>
    /// Never throws. The email this goes into matters more than the box does, and every caller has
    /// already done something durable — stored ideas, claimed a throttle slot — by the time it asks.
    /// A box that failed to compose is left out rather than taking the email with it.
    ///
    /// A token is issued only when the box is shown, so that nobody accumulates live links they were
    /// never sent.
    /// </remarks>
    public async Task<string> ComposeForAsync(Guid participantId)
    {
        if (participantId == Guid.Empty)
            return string.Empty;

        try
        {
            var facts = await _giftExchangeProvider
                .GetInvitationReminderAsync(participantId)
                .ConfigureAwait(false);

            if (!facts.IsNeeded)
                return string.Empty;

            // Proves the invitation was seen, unlike the Ask's: every button on the page this opens
            // sits under the name they drew.
            var token = await _giftExchangeProvider
                .IssueGiftIdeaTokenAsync(new IssueGiftIdeaTokenRequest
                {
                    ParticipantId = participantId,
                    ProvesInvitationSeen = true
                })
                .ConfigureAwait(false);

            return Compose(facts, token);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not compose an invitation reminder; sending without one.");
            return string.Empty;
        }
    }

    /// <summary>Where the copy of a participant's invitation is served, for this token.</summary>
    internal static string InvitationUrlFor(string token) =>
        $"{Branding.InvitationUrl}/{HttpUtility.UrlEncode(token)}";

    /// <remarks>
    /// A single-cell table rather than a styled div, because Outlook's Word engine draws a cell's
    /// background and ignores a div's.
    /// </remarks>
    internal static string Compose(InvitationReminderFacts facts, string token)
    {
        var organizerName = HttpUtility.HtmlEncode(facts.OrganizerName);
        var subject = HttpUtility.HtmlEncode(EmailCompositionService.GetSubject(facts.OrganizerName, facts.HatName));
        var sentOn = facts.SentAt.UtcDateTime.ToString("MMMM d", CultureInfo.InvariantCulture);
        var url = HttpUtility.HtmlAttributeEncode(InvitationUrlFor(token));

        return $"""
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="border-collapse:collapse;width:100%;">
                <tr>
                <td style="background-color:#fdf3dc;border-left:4px solid #e0a526;padding:14px 18px;">
                <b>First time hearing of this gift exchange?</b><br /><br />
                {organizerName} added you to it, and we emailed you an invitation around {sentOn}. It may have gone to your spam folder or to another folder, especially if you use Gmail, where it may be under Promotions.<br /><br />
                Search your email for <b>&ldquo;{subject}&rdquo;</b> to find it, or <a href="{url}">see your invitation on our website</a>.
                </td>
                </tr>
                </table>
                """;
    }
}
