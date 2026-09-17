using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// The gift ideas emails: the buttons that lead to the share page, the asks, and what goes on to the
/// person somebody's ideas are for.
/// </summary>
/// <remarks>
/// Everything a sender wrote is HTML-encoded on its way into these bodies and none of it is ever
/// turned into a link. That second part is deliberate and is most of what makes allowing links
/// tolerable: a URL arrives as text, so the person reading it sees where it actually goes rather
/// than words an anchor was wrapped around. Some mail clients will make it clickable at the far
/// end, which is fine — what matters is that the address is visible either way, and that this
/// application is not the thing that hid it.
/// </remarks>
[UsedImplicitly]
public class GiftIdeaEmailCompositionService
{
    public static string ForwardSubject(string senderName) =>
        $"{senderName} shared gift ideas with you";

    public static string AskSubject(string hatName) =>
        $"What would you like for {GiftExchangeNaming.Describe(hatName)}?";

    /// <summary>
    /// Sent to somebody asked for ideas about another participant.
    /// </summary>
    /// <remarks>
    /// Names the subject, unlike <see cref="AskSubject"/>, which cannot: that one goes to the
    /// person it is about, and naming them in the subject line would read as though we were telling
    /// them something they did not know. Here the name is the whole point — a subject line reading
    /// "can you help with a gift?" tells the reader nothing about whether they can.
    /// </remarks>
    public static string ContributionAskSubject(string subjectName) =>
        $"Any gift ideas for {subjectName}?";

    public static string ContributionForwardSubject(string helperName, string subjectName) =>
        $"{helperName} shared gift ideas for {subjectName}";

    /// <summary>
    /// Sent when an Ask went out to some of the people chosen but not all of them.
    /// </summary>
    /// <remarks>
    /// Says outright that something did not happen. The email exists only in that case, so a
    /// subject line hedging about the outcome would waste the one line the reader is certain to
    /// see.
    /// </remarks>
    public static string AskPartiallySentSubject => "We couldn't ask everyone you chose";

    /// <summary>Where the Ask button points. The API, not the front end.</summary>
    private const string AskUrl = "https://api.namesoutofahat.com/ask";

    /// <summary>Where the SHARE GIFT IDEAS button points. The API, like the Ask.</summary>
    private const string ShareIdeasUrl = "https://api.namesoutofahat.com/ideas";

    /// <summary>
    /// The block inviting somebody to share gift ideas about themselves, addressed to the token
    /// issued to them.
    /// </summary>
    /// <remarks>
    /// Shared between the invitation and the Ask, so that both carry the identical block and the
    /// wording cannot drift between them.
    /// </remarks>
    public static string BuildShareGiftIdeasBlock(string giftIdeasToken) =>
        BuildShareBlock(
            giftIdeasToken,
            "SHARE GIFT IDEAS",
            "Click above to share gift ideas with only the person who picked your name. Nobody else in the exchange will see them &mdash; not even the organizer.");

    /// <summary>
    /// The same block, for somebody suggesting gifts for another participant rather than for
    /// themselves.
    /// </summary>
    /// <remarks>
    /// Says that the ideas will be attributed. That is the one thing about this arrangement the
    /// helper cannot work out for themselves — the asker is anonymous to them, so the natural
    /// assumption is that the anonymity runs both ways — and somebody who would rather stay out of
    /// it is entitled to know before they write anything, not afterwards.
    /// </remarks>
    public static string BuildShareIdeasAboutBlock(string subjectName, string askToken)
    {
        var encodedName = HttpUtility.HtmlEncode(subjectName);

        return BuildShareBlock(
            askToken,
            $"SHARE GIFT IDEAS FOR {HttpUtility.HtmlEncode(subjectName.ToUpperInvariant())}",
            $"Click above to send your ideas to the person shopping for {encodedName}. They'll see the ideas came from you. Nobody else will &mdash; not {encodedName}, and not the organizer.");
    }

    /// <summary>
    /// A button leading to the share page, and the sentence explaining it.
    /// </summary>
    /// <remarks>
    /// An ordinary link to a page rather than anything that happens in the reader's mail client.
    /// The page renders a form and nothing more, so a mail scanner fetching the link on delivery
    /// shares nothing; only the button on that page does.
    ///
    /// One implementation behind both callers, so that the parts neither of them should be free to
    /// reword &mdash; where the button goes, and what happens after clicking it &mdash; cannot
    /// drift apart.
    /// </remarks>
    private static string BuildShareBlock(
        string token,
        string buttonLabel,
        string explanation
    )
    {
        var url = $"{ShareIdeasUrl}/{HttpUtility.UrlEncode(token)}";

        return $"""
                <a href="{HttpUtility.HtmlAttributeEncode(url)}" style="background-color:#1f7a4d;color:#ffffff;padding:12px 22px;text-decoration:none;border-radius:4px;display:inline-block;font-weight:bold;">{buttonLabel}</a>
                <br /><br />
                {explanation} The button opens a page where you can type your ideas, and change them later if you think of something better.
                """;
    }

    /// <summary>
    /// The block offering to ask for gift ideas about the recipient's own pick, on their behalf.
    /// </summary>
    /// <remarks>
    /// Lands on a page instead of performing the Ask outright, as the share button does, and that is
    /// deliberate: a link in an
    /// email is fetched by mail security scanners before anybody reads it, so a link that acted
    /// immediately would send the Ask on delivery — burning the throttle window and mailing
    /// somebody on behalf of a person who never clicked anything.
    ///
    /// The button no longer names a single person to be asked, because the page behind it offers
    /// the whole exchange. It names the person the ideas are wanted <em>about</em> instead, which
    /// is the part the reader already has in mind — they have just been told that name.
    /// </remarks>
    public static string BuildAskBlock(string pickedName, string askToken)
    {
        var encodedName = HttpUtility.HtmlEncode(pickedName);
        var url = $"{AskUrl}/{HttpUtility.UrlEncode(askToken)}";

        return $"""
                <a href="{HttpUtility.HtmlAttributeEncode(url)}" style="background-color:#2f5d8a;color:#ffffff;padding:12px 22px;text-decoration:none;border-radius:4px;display:inline-block;font-weight:bold;">ANONYMOUSLY ASK ABOUT GIFTS FOR {HttpUtility.HtmlEncode(pickedName.ToUpperInvariant())}</a>
                <br /><br />
                Click above to ask {encodedName} what they'd like &mdash; or, if asking {encodedName} directly would give the game away, to ask anyone else in the exchange what they think {encodedName} would like. You can ask several people at once. Your name will not be revealed to any of them, and anything they share comes straight to you.
                """;
    }

    /// <summary>
    /// Carries one participant's ideas to the person who drew them.
    /// </summary>
    /// <remarks>
    /// Comes from the no-reply address and carries no way back to the sender, which is not a
    /// courtesy but the whole arrangement: a reply that reached them would tell them who holds
    /// their name. The recipient already knows whose name they drew, so naming the sender here
    /// gives away nothing.
    /// </remarks>
    public string ComposeForward(string senderName, string hatName, string ideas) =>
        Wrap([
            $"<b>{HttpUtility.HtmlEncode(senderName)}</b>, whose name you picked in {HttpUtility.HtmlEncode(GiftExchangeNaming.Describe(hatName))}, shared some gift ideas with you:",
            Quote(ideas),
            "<i>You're the only person seeing this. Please don't reply to this email — your reply would reveal that you have their name.</i>",
            BuildLinkDisclaimer()
        ]);

    /// <summary>
    /// Sent to the person somebody has asked for gift ideas.
    /// </summary>
    /// <remarks>
    /// Names nobody. The whole promise of the button that triggers this is anonymity, and in an
    /// exchange this small, naming the asker would be naming the one person holding this
    /// recipient's name. "Someone" is doing real work in that first line.
    ///
    /// It also gives nothing away by existing: everybody is drawn by exactly one person, so
    /// learning that somebody has your name tells you what you already knew.
    /// </remarks>
    public string ComposeAsk(string hatName, string giftIdeasToken) =>
        Wrap([
            $"Someone in {HttpUtility.HtmlEncode(GiftExchangeNaming.DescribeMidSentence(hatName))} would like to know what you'd like.",
            "They picked your name, and they're hoping for a hint. You can share as much or as little as you like.",
            BuildShareGiftIdeasBlock(giftIdeasToken),
            "<i>We won't tell you who asked, and we won't tell them we passed the message on.</i>"
        ]);

    /// <summary>
    /// Sent to somebody a participant has asked for ideas about another participant.
    /// </summary>
    /// <remarks>
    /// Names the subject and nobody else. Whoever asked stays out of it, which is the promise the
    /// button makes — and in a small exchange it is a promise this email cannot fully keep, since a
    /// reader who works out that only one person could have drawn the subject has their answer. The
    /// page that sends this says so plainly to the one person who can weigh it, which is the asker.
    /// Repeating it here would only hand the helper the deduction.
    ///
    /// Says that anything shared will be attributed, before the button rather than after it.
    /// </remarks>
    public string ComposeContributionAsk(string hatName, string subjectName, string askToken)
    {
        var encodedName = HttpUtility.HtmlEncode(subjectName);

        return Wrap([
            $"Someone in {HttpUtility.HtmlEncode(GiftExchangeNaming.DescribeMidSentence(hatName))} drew {encodedName}'s name, and they're hoping you might know what {encodedName} would like.",
            $"Anything helps &mdash; something {encodedName} has been after, a hobby, a size, a shop they like, or a link to something you've seen them admire. You can share as much or as little as you want, and you're welcome to ignore this.",
            BuildShareIdeasAboutBlock(subjectName, askToken),
            $"<i>We won't tell you who asked. {encodedName} isn't being told about this either, and nothing you send goes to them &mdash; only to the person shopping for them.</i>"
        ]);
    }

    /// <summary>
    /// Sent to an asker when some of the people they chose were not asked.
    /// </summary>
    /// <remarks>
    /// Only when something was refused. Every outcome is already on the page they are looking at
    /// when they submit, and an email repeating a round that went through exactly as asked would be
    /// one more message in an inbox for no reason. A round that fell short is different: the page
    /// is gone the moment they close the tab, and what they need to remember is the part that did
    /// not happen.
    ///
    /// Names each person and the date, because the likeliest reader is somebody who does not
    /// remember asking — and being told a date is what turns "you already asked" from an accusation
    /// into a fact they can check.
    /// </remarks>
    public string ComposeAskSummary(string subjectName, ImmutableList<AskAttempt> attempts)
    {
        var encodedName = HttpUtility.HtmlEncode(subjectName);
        var sent = attempts.Where(attempt => attempt.Sent).ToImmutableList();
        var skipped = attempts.Where(attempt => !attempt.Sent).ToImmutableList();

        var lines = new List<string>
        {
            sent.IsEmpty
                ? $"We weren't able to ask anyone for gift ideas for {encodedName} just now."
                : $"We asked {NameFormatting.ToSentenceList(sent.Select(attempt => attempt.Name))} for gift ideas for {encodedName}, without saying who wanted to know."
        };

        // Defensive: nothing sends this email unless somebody was skipped, and a list with no names
        // under a sentence introducing them would read as a fault in the application.
        if (!skipped.IsEmpty)
        {
            lines.Add("We didn't ask these people, because you asked them recently:");
            lines.Add(Block(string.Join(
                "<br />",
                skipped.Select(attempt => attempt.PreviouslyAskedAt == DateTimeOffset.MinValue
                    ? $"{HttpUtility.HtmlEncode(attempt.Name)} &mdash; asked recently"
                    : $"{HttpUtility.HtmlEncode(attempt.Name)} &mdash; asked on {attempt.PreviouslyAskedAt:d MMMM yyyy}"))));
            lines.Add("You can ask each of them again after a week. Nobody is told how many people you've asked, or how often.");
        }

        lines.Add("If any of them shares anything, we'll send it to you as soon as they do.");

        return Wrap(lines);
    }

    /// <summary>
    /// Carries one participant's suggestions about another to the person who asked for them.
    /// </summary>
    /// <remarks>
    /// Names the helper, which the ordinary forward also does and for a stronger reason here: the
    /// asker chose who to ask, so this tells them nothing they did not already know, and when
    /// several people answer, an unattributed pile of suggestions is much less use than an
    /// attributed one.
    ///
    /// Comes from the no-reply address and carries no way back, like everything else here. There is
    /// nothing secret about the asker's identity to protect from the helper at this point — the
    /// helper never sees this message — but a reply that reached them would tell them who asked.
    /// </remarks>
    public string ComposeContributionForward(string helperName, string subjectName, string hatName, string ideas) =>
        Wrap([
            $"<b>{HttpUtility.HtmlEncode(helperName)}</b> has some ideas about what {HttpUtility.HtmlEncode(subjectName)} might like, after you asked in {HttpUtility.HtmlEncode(GiftExchangeNaming.Describe(hatName))}:",
            Quote(ideas),
            $"<i>You're the only person we've shown this to, and we didn't tell {HttpUtility.HtmlEncode(helperName)} who was asking. Please don't reply to this email &mdash; nobody reads this address.</i>",
            BuildLinkDisclaimer()
        ]);

    /// <summary>
    /// Says plainly that links are the sender's own and are not checked.
    /// </summary>
    /// <remarks>
    /// The same honesty the invitation small print already practises about organizer-supplied text.
    /// Nothing here can tell whether a link is safe, and implying otherwise by staying quiet would
    /// be worse than saying so.
    /// </remarks>
    private static string BuildLinkDisclaimer() =>
        """
        <small style="color:#666666;">
        These are their own words, including any links. We don't check where links go, so only follow one if you trust it. Links are shown in full so you can see where they lead before clicking.
        </small>
        """;

    /// <summary>
    /// Renders submitted text as an indented block, encoded and with line breaks preserved.
    /// </summary>
    /// <remarks>
    /// <c>HtmlEncode</c> first and then newlines to breaks, in that order. Reversed, the tags this
    /// inserts would themselves be encoded and the recipient would read the markup.
    /// </remarks>
    private static string Quote(string ideas) =>
        Block(HttpUtility.HtmlEncode(ideas).Replace("\n", "<br />"));

    /// <summary>
    /// The indented frame, around markup this class built rather than around somebody's text.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Quote"/> and never given anything a sender wrote. Everything that
    /// arrives from outside goes through Quote, which encodes it first; the split exists so that
    /// the encoding step cannot be skipped by a caller reaching for the frame it wanted.
    /// </remarks>
    private static string Block(string html) =>
        $"""
         <div style="border-left:3px solid #cccccc;padding-left:12px;color:#333333;">
         {html}
         </div>
         """;

    private static string Wrap(IEnumerable<string> lines) =>
        EmailBranding.Masthead()
        + "<br /><br />"
        + string.Join(
            "<br /><br />",
            lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        + "<br /><br />";
}
