using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// The pages somebody sees after clicking a SHARE GIFT IDEAS button.
/// </summary>
/// <remarks>
/// Hand-written HTML with inline styles and no scripts, for the reason <see cref="AskPageComposer"/>
/// gives. The shell around each page is <see cref="EmailLinkedPage"/>.
///
/// Nothing here ever names the writer's own pick. The link is the only credential, so whoever holds
/// it sees these pages, and the pick is the one thing this application keeps quiet.
/// </remarks>
[UsedImplicitly]
public class ShareIdeasPageComposer
{
    private const string ShareIdeasUrl = "https://api.namesoutofahat.com/ideas";

    /// <summary>The field the textarea is posted under.</summary>
    public const string IdeasField = "ideas";

    /// <summary>
    /// The form: a box to write in, and who will see what is written.
    /// </summary>
    /// <remarks>
    /// Says who will read the ideas before the box rather than after the button, and says it
    /// differently for the two kinds of submission. A helper writing about somebody else is named
    /// to the person who asked, and they are entitled to know that before they write anything.
    /// </remarks>
    internal string ComposeForm(ComposeShareIdeasFormRequest request)
    {
        var route = request.Route;
        var encodedSubject = HttpUtility.HtmlEncode(route.Subject.Name);
        var action = $"{ShareIdeasUrl}/{HttpUtility.UrlEncode(request.Token)}";

        var body = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(request.Notice))
            body.Append(
                $"""
                 <p style="margin:0 0 20px;padding:12px 16px;background-color:#fdf3d8;border-radius:4px;">{request.Notice}</p>
                 """);

        body.Append(route.IsContribution
            ? $"""
               <p>Your ideas go to the person shopping for {encodedSubject}. <b>They'll see the ideas
               came from you.</b> Nobody else will &mdash; not {encodedSubject}, and not the
               organizer.</p>
               """
            : """
              <p>Your ideas go to the person who picked your name, and <b>nobody else</b> &mdash; not
              the organizer, and nobody else in the exchange.</p>
              """);

        if (request.HasSharedBefore)
            body.Append(
                """
                <p>This is what you shared last time. Change it and send it again, and we'll pass the
                new version on.</p>
                """);

        body.Append(
            $"""
             <form method="post" enctype="multipart/form-data" action="{HttpUtility.HtmlAttributeEncode(action)}">
               <textarea name="{IdeasField}" rows="10" maxlength="{GiftIdeaContentPolicy.MaxLength}" style="box-sizing:border-box;width:100%;padding:12px;border:1px solid #cccccc;border-radius:4px;font:inherit;">{HttpUtility.HtmlEncode(request.Ideas)}</textarea>
               <p style="margin:20px 0 0;">
                 <button type="submit" style="background-color:#1f7a4d;color:#ffffff;padding:12px 22px;border:0;border-radius:4px;font-weight:bold;font-size:16px;cursor:pointer;">
                   Share
                 </button>
               </p>
             </form>
             <p style="color:#666666;font-size:14px;">Up to {GiftIdeaContentPolicy.MaxLength:N0} characters.
             Links are welcome &mdash; paste the full web address rather than a shortened one. Please
             don't mention the name of the person you drew.</p>
             """);

        return Page(
            route.IsContribution ? $"Gift ideas for {route.Subject.Name}" : "Share your gift ideas",
            body.ToString());
    }

    /// <summary>
    /// What was sent, word for word.
    /// </summary>
    /// <remarks>
    /// The echo that used to arrive by email. Nothing about the text is guessed at any more, but
    /// seeing exactly what went is still the reassurance somebody wants after pressing a button.
    /// </remarks>
    internal string ComposeShared(GiftIdeaRoute route, string ideas)
    {
        var encodedSubject = HttpUtility.HtmlEncode(route.Subject.Name);

        var intro = route.IsContribution
            ? $"<p>Thanks &mdash; your ideas are on their way to the person shopping for {encodedSubject}. They'll see that these came from you.</p>"
            : "<p>Thanks &mdash; your gift ideas are on their way to the person who picked your name.</p>";

        return Page(
            "Shared!",
            $"""
             {intro}
             <p>Here's exactly what we sent:</p>
             <div style="border-left:3px solid #cccccc;padding-left:12px;color:#333333;">
             {HttpUtility.HtmlEncode(ideas).Replace("\n", "<br />")}
             </div>
             <p>Thought of something else? Use the same button in your email to change what you
             shared, and we'll send the new version.</p>
             <p>You can close this page.</p>
             """);
    }

    /// <summary>
    /// One page for every reason ideas cannot be shared from this link.
    /// </summary>
    /// <remarks>
    /// Deliberately does not distinguish an unknown token from a finished exchange, for the reason
    /// <see cref="AskPageComposer.ComposeUnavailable"/> gives.
    /// </remarks>
    public static string ComposeUnavailable() =>
        Page(
            "This link isn't available",
            """
            <p>We can't share gift ideas from this link. The gift exchange may have finished, or the
            link may have expired.</p>
            <p>If the exchange is still running, use the button in your most recent email about
            it.</p>
            """);

    /// <summary>
    /// Why a submission was refused, and what to do about it.
    /// </summary>
    /// <remarks>
    /// Every branch says what to do next. The text stays in the form underneath, so the fix is an
    /// edit rather than starting again.
    /// </remarks>
    public static string ExplainRefusal(GiftIdeaSubmissionOutcome outcome) =>
        outcome switch
        {
            GiftIdeaSubmissionOutcome.RejectedNothingToShare =>
                "Write your ideas in the box, then press Share.",

            GiftIdeaSubmissionOutcome.RejectedTooLong =>
                $"That's longer than we can pass on. Please shorten it to {GiftIdeaContentPolicy.MaxLength:N0} characters or fewer.",

            // Said carefully. The likeliest cause is text pasted from the invitation, which is an
            // easy mistake and not a suspicious one.
            GiftIdeaSubmissionOutcome.RejectedWouldRevealTheirPick =>
                "What you wrote mentions the name of the person you picked. We can't pass that on &mdash; it would tell the person reading it whose name you drew. Please take the name out.",

            GiftIdeaSubmissionOutcome.RejectedShortenedLink =>
                "What you wrote contains a shortened link. We can't show the person receiving it where a shortened link leads, so please paste the full web address instead.",

            GiftIdeaSubmissionOutcome.RejectedSelfReferentialLink =>
                "What you wrote links back to namesoutofahat.com. We don't pass those on. Please take the link out.",

            GiftIdeaSubmissionOutcome.RejectedTooManyLinks =>
                $"What you wrote contains more than {GiftIdeaContentPolicy.MaxLinks} links. Please send a shorter list.",

            GiftIdeaSubmissionOutcome.RejectedInappropriateContent =>
                "What you wrote contains content we can't pass on. Please reword it.",

            // Distinct from the line above on purpose: the text may be perfectly fine, and calling
            // it inappropriate when the checker was simply unreachable is wrong and unhelpful.
            GiftIdeaSubmissionOutcome.RejectedModerationUnavailable =>
                "We couldn't check what you wrote just now. Please try again in a few minutes.",

            _ => "Something went wrong at our end. Please try again."
        };

    private static string Page(string heading, string body) =>
        EmailLinkedPage.Compose(heading, body);
}
