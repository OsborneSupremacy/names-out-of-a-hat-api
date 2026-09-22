using System.Web;

namespace GiftExchange.Library.Services;

[UsedImplicitly]
public class EmailCompositionService
{
    /// <summary>
    /// One participant's invitation.
    /// </summary>
    /// <remarks>
    /// Internal, because <see cref="ComposeInvitationRequest"/> is. Nothing outside this assembly
    /// composes an invitation — the three callers are the send, the resend behind an address
    /// correction, and the organizer's preview.
    /// </remarks>
    internal string ComposeEmail(ComposeInvitationRequest request)
    {
        var hat = request.Hat;
        var pickedName = request.PickedName;

        // Everything below originates with the organizer or a participant. The subject is plain
        // text and must not be encoded, but every value placed into this HTML body must be.
        var organizerName = HttpUtility.HtmlEncode(hat.Organizer.Name);
        var organizerEmail = HttpUtility.HtmlEncode(hat.Organizer.Email);

        var lines = new List<string>
        {
            EmailBranding.Masthead(),
            $"Dear {HttpUtility.HtmlEncode(request.ParticipantName)},",
            GetGreeting(hat, organizerName, organizerEmail),
            "The person whose name was picked out of a hat for you is:",
            // The face is not encoded and does not need to be: it is one of a closed list this
            // application owns, never anything a participant or an organizer typed.
            $"<b>{request.PickedEmoji} {HttpUtility.HtmlEncode(pickedName)}</b>"
        };

        // Right after the name, because the first thing somebody does with a name is work out how
        // long they have. Nothing here is typed by anybody, so there is nothing to encode.
        if (hat.ExchangeDate != DateOnly.MinValue)
            lines.Add(ExchangeDatePhrasing.Describe(hat.ExchangeDate));

        // Encoded after the sentence is built rather than before: the words around the organizer's
        // text are fixed and carry nothing to encode, and PriceRangePhrasing works on what they
        // actually typed rather than on an escaped version of it.
        if (!string.IsNullOrWhiteSpace(hat.PriceRange))
            lines.Add(HttpUtility.HtmlEncode(PriceRangePhrasing.Describe(hat.PriceRange)));

        if (!string.IsNullOrWhiteSpace(hat.AdditionalInformation))
            lines.Add(HttpUtility.HtmlEncode(hat.AdditionalInformation.Trim()));

        if (!string.IsNullOrWhiteSpace(request.GiftIdeasToken))
        {
            // Ask first, then share, then offer. The order matches what somebody opening this
            // actually wants to do: they have just been told a name, and the immediate question is
            // what that person wants, not what they themselves want. Offering ideas about somebody
            // else comes last because it is the only one of the three that is not about the
            // reader's own exchange with their pick -- it is a favour to somebody they will never
            // be told the name of.
            lines.Add(GiftIdeaEmailCompositionService.BuildAskBlock(pickedName, request.GiftIdeasToken));
            lines.Add(GiftIdeaEmailCompositionService.BuildShareGiftIdeasBlock(request.GiftIdeasToken));
            lines.Add(GiftIdeaEmailCompositionService.BuildOfferIdeasBlock(request.GiftIdeasToken));
        }

        lines.AddRange([
            $"""If you have any questions, contact <a href="mailto:{HttpUtility.HtmlAttributeEncode(hat.Organizer.Email)}">{organizerName}</a>.""",
            "<i>Please do not reply to this email or share it with anyone else in the gift exchange. Only you know whose name you were assigned!</i>",
            BuildSmallPrint(organizerEmail, request.LeaveToken)
        ]);

        var body = new StringBuilder();

        foreach (var line in lines)
        {
            body.Append(line);
            body.AppendLine("<br /><br />");
        }

        return body.ToString();
    }

    /// <summary>
    /// The body names the organizer's address as well as their name, so a recipient can see who
    /// the exchange actually came from rather than only a display name somebody chose.
    /// </summary>
    private static string GetGreeting(Hat hat, string organizerName, string organizerEmail) =>
        $"{organizerName} ({organizerEmail}) has added you to {HttpUtility.HtmlEncode(GiftExchangeNaming.Describe(hat.Name))}!";

    public static string GetSubject(Hat hat) =>
        $"{hat.Organizer.Name} has added you to {GiftExchangeNaming.Describe(hat.Name)}!";

    /// <summary>
    /// The footer every invitation carries: who it came from, what this service checks, what it
    /// does not stand behind, and how to get out.
    /// </summary>
    /// <remarks>
    /// The second paragraph is a claim about what actually happens, so it is worth keeping true. An
    /// organizer signs in by email before they can send anything, and every piece of text an
    /// organizer supplies that can reach this email — the exchange name, their own name, the
    /// participant names, the price range and the additional information — passes through
    /// <see cref="ContentModerationService"/> on its way in. "Automatically" is doing real work in
    /// that sentence: nobody reads these, and the paragraph after it is what that costs.
    ///
    /// The leave link is last and in the same small grey type as the rest, which is where it
    /// belongs. Somebody who wants out will look for it and find it; putting it above the fold
    /// would make an ordinary invitation read as though leaving were the expected response to it,
    /// and this is the only message most participants get about an exchange they are pleased to be
    /// in. It is the same placement an unsubscribe line takes, and for the same reasons.
    /// </remarks>
    /// <param name="leaveToken">
    /// Empty for the organizer, who is never issued one — so the sentence is simply not written
    /// rather than being written and pointed nowhere. Also empty on a preview of an invitation the
    /// organizer has not sent yet.
    /// </param>
    private static string BuildSmallPrint(string organizerEmail, string leaveToken)
    {
        var footer = new StringBuilder();

        footer.Append(
            $"""
             <small style="color:#666666;">
             This email was sent on behalf of {organizerEmail} through <a href="{Branding.SiteUrl}">namesoutofahat.com</a>, a free app for running gift exchanges where names are drawn at random.
             <br /><br />
             Organizers confirm their email address before they can send invitations, and everything they write is screened automatically for illegal and inappropriate content.
             <br /><br />
             Beyond those checks, the gift exchange name, the participant names and any additional information are the organizer's own words, and namesoutofahat.com is not responsible for them.
             """);

        if (!string.IsNullOrWhiteSpace(leaveToken))
        {
            var leaveUrl = $"{Branding.LeaveUrl}/{HttpUtility.UrlEncode(leaveToken)}";

            footer.Append(
                $"""
                 <br /><br />
                 If you'd rather not take part, you can <a href="{HttpUtility.HtmlAttributeEncode(leaveUrl)}">leave this gift exchange</a>. We'll let the organizer know somebody left, without saying who.
                 """);
        }

        footer.Append("</small>");

        return footer.ToString();
    }
}
