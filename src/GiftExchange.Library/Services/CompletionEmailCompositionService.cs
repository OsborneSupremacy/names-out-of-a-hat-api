using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// The message every participant receives once the organizer says the exchange has happened.
/// </summary>
/// <remarks>
/// The one email this application sends that keeps nothing back. Every other message is written
/// around the fact that a participant must not learn who drew whom; this one goes out only after
/// the organizer has confirmed the exchange is over and the picks are theirs to reveal, so the
/// whole draw is in it, for the record.
///
/// It is also the last thing anybody in the exchange hears from us, so it says who it came from and
/// how to reach them rather than ending on the list.
/// </remarks>
[UsedImplicitly]
public class CompletionEmailCompositionService
{
    /// <summary>
    /// Plain text, unlike the body: a subject is not HTML and encoding it would show the entities.
    /// </summary>
    public static string GetSubject(Hat hat) =>
        $"{GiftExchangeNaming.DescribeToOpenASentence(hat.Name)} has finished";

    public string ComposeEmail(Hat hat, string participant)
    {
        var organizerName = HttpUtility.HtmlEncode(hat.Organizer.Name);
        var organizerEmail = HttpUtility.HtmlEncode(hat.Organizer.Email);

        var lines = new List<string>
        {
            EmailBranding.Masthead(),
            $"Dear {HttpUtility.HtmlEncode(participant)},",
            $"{organizerName} ({organizerEmail}) has let us know that {HttpUtility.HtmlEncode(GiftExchangeNaming.DescribeMidSentence(hat.Name))} is over.",
            "We hope everybody came away with something they liked."
        };

        // Defensive rather than a shape anything sends: an exchange cannot reach this point without
        // having been shaken. An exchange with no draw to show still gets the rest of the email.
        var draw = BuildDraw(hat);

        if (!string.IsNullOrEmpty(draw))
        {
            lines.Add("Here's who picked whose name, for the record:");
            lines.Add(draw);
        }

        lines.AddRange([
            $"""If you have any questions, contact <a href="mailto:{HttpUtility.HtmlAttributeEncode(hat.Organizer.Email)}">{organizerName}</a>.""",
            BuildSmallPrint(organizerEmail)
        ]);

        return string.Join("<br /><br />", lines) + "<br /><br />";
    }

    /// <summary>
    /// The whole draw, one row per participant.
    /// </summary>
    /// <remarks>
    /// A table rather than lines of running text because the names in a row have to line up with
    /// the names above them, and neither a name nor a face is a fixed width in any font this is
    /// read in. Every name and every face gets a cell of its own, so the columns are as wide as
    /// the longest thing in them and each row turns in the same place.
    ///
    /// Both people in a row are marked, the same way round as in the invitation, so somebody
    /// reading this next to the invitation they were sent sees the same person marked the same way.
    /// It is the face that person wears in this hat, looked up rather than derived from the name,
    /// which is what makes the two messages agree even after an organizer has changed one. A
    /// participant with no face to show leaves that cell empty and the row still lines up.
    ///
    /// Two people in a hat may share a name, and a list in which "Sam" gives to "Sam" says nothing,
    /// so both columns name people the way <see cref="ParticipantNaming"/> does.
    ///
    /// Not encoded, and it does not need to be: a face is one of a closed list this application
    /// owns. The names around it are the organizer's words and are encoded.
    /// </remarks>
    private static string BuildDraw(Hat hat)
    {
        var rows = hat.Participants
            .Where(participant => !string.IsNullOrWhiteSpace(participant.PickedRecipient.Email))
            .Select(participant =>
                $"""
                 <tr>
                 <td style="padding:0 6px 4px 0;">{participant.Emoji}</td>
                 <td style="padding:0 10px 4px 0;">{HttpUtility.HtmlEncode(hat.DisplayNameIn(participant.Person))}</td>
                 <td style="padding:0 10px 4px 0;">&rarr;</td>
                 <td style="padding:0 6px 4px 0;">{hat.EmojiFor(participant.PickedRecipient)}</td>
                 <td style="padding:0 0 4px 0;"><b>{HttpUtility.HtmlEncode(hat.DisplayNameIn(participant.PickedRecipient))}</b></td>
                 </tr>
                 """)
            .ToList();

        return rows.Count == 0
            ? string.Empty
            : $"""
               <div style="border-left:3px solid #cccccc;padding-left:12px;color:#333333;">
               <table cellpadding="0" cellspacing="0" border="0" style="border-collapse:collapse;">
               {string.Join("\n", rows)}
               </table>
               </div>
               """;
    }

    /// <summary>
    /// Shorter than the invitation's. Nothing here is the organizer's own words except the names
    /// they entered, and the disclaimer that footer carries has already been made once.
    /// </summary>
    private static string BuildSmallPrint(string organizerEmail) =>
        $"""
         <small style="color:#666666;">
         This email was sent on behalf of {organizerEmail} through <a href="https://namesoutofahat.com">namesoutofahat.com</a>, a free app for running gift exchanges where names are drawn at random.
         <br /><br />
         Nobody reads replies to this address, so please contact the organizer directly if you need to.
         </small>
         """;
}
