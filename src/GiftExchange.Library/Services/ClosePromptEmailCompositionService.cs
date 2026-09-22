using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// The message an organizer gets a week after their exchange's date, if they have not closed it.
/// </summary>
/// <remarks>
/// Closing is the one thing in the application nobody is prompted to do. An organizer sends the
/// invitations, the party happens, and the exchange sits open forever: nobody gets the list of who
/// drew whom, and next year's copy -- which needs a closed exchange -- is not offered. This is
/// the nudge.
///
/// It asks and does not act. Closing reveals every pick to everybody and cannot be undone, and the
/// date it is measured from is one the organizer called approximate. So the email says what closing
/// does and leaves the button to them.
///
/// Written to the organizer alone and names nobody. Nothing about the draw is in it, for the reason
/// the undeliverable notice gives: an organizer who is also a participant must not learn from an
/// administrative email something the invitation kept from them.
///
/// Sent by <see cref="AutomaticEmailSender"/> for the same reason that notice is: it is not a
/// message to a participant, and tagging it against the organizer's participant row would overwrite
/// what that row says about their invitation.
/// </remarks>
[UsedImplicitly]
internal class ClosePromptEmailCompositionService
{
    internal static string GetSubject(Hat hat) =>
        $"Has your gift exchange happened? {GiftExchangeNaming.Describe(hat.Name)}";

    internal string ComposeEmail(Hat hat)
    {
        var organizerName = HttpUtility.HtmlEncode(hat.Organizer.Name);
        var exchangeName = HttpUtility.HtmlEncode(GiftExchangeNaming.Describe(hat.Name));

        var lines = new List<string>
        {
            EmailBranding.Masthead(),
            $"Dear {organizerName},",
            $"{HttpUtility.HtmlEncode(GiftExchangeNaming.DescribeToOpenASentence(hat.Name))} was planned for around {ExchangeDatePhrasing.Format(hat.ExchangeDate)}, and it's still open.",
            // What closing does, before the link to do it. It is irreversible, and somebody should
            // know that before they click rather than after.
            "If the gifts have been exchanged, you can close it now. Closing emails everybody the full list of who drew whom, and it can't be undone.",
            $"""<a href="{HttpUtility.HtmlAttributeEncode(Branding.HatUrl(hat.Id))}">Open {exchangeName}</a> and use <b>Reveal Picked Names</b>.""",
            // The reason to bother that is about the organizer rather than about everybody else.
            "Once it's closed, you can copy it into next year's exchange with the same people, and with everybody's recipient from this year left out automatically.",
            "<i>If it hasn't happened yet, there's nothing to do. Close it whenever it has.</i>",
            BuildSmallPrint()
        };

        return string.Join("<br /><br />", lines) + "<br /><br />";
    }

    /// <summary>
    /// Says why this arrived and that it will not arrive again, and states the retention rule,
    /// which this is the only email to mention.
    /// </summary>
    private static string BuildSmallPrint() =>
        $"""
         <small style="color:#666666;">
         You're getting this because you organize a gift exchange at <a href="{Branding.SiteUrl}">namesoutofahat.com</a> and gave a date for it. It's sent once, a week after that date, and only while the exchange is still open.
         <br /><br />
         Gift exchanges with a date are deleted {ExchangeDates.RetentionMonths} months after it, closed or not.
         <br /><br />
         Nobody reads replies to this address.
         </small>
         """;
}
