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
///
/// There is a second thing these pages keep quiet, and it is newer: whether anybody has asked for
/// ideas about the person reading them. A participant who holds their ideas back is told the same
/// words whether they are still sitting here or have just been sent, because the difference is the
/// asking, and knowing about the asking is what the Ask lets somebody avoid.
/// </remarks>
[UsedImplicitly]
public class ShareIdeasPageComposer
{
    private const string ShareIdeasUrl = "https://api.namesoutofahat.com/ideas";

    /// <summary>The field the textarea is posted under.</summary>
    public const string IdeasField = "ideas";

    /// <summary>
    /// The field the "only if they ask" checkbox is posted under.
    /// </summary>
    /// <remarks>
    /// Read by its presence rather than its value, which is what an unchecked box not being posted at
    /// all means. Public for the same reason <see cref="IdeasField"/> is: the parser and the tests
    /// both build bodies from it, and a name spelled twice is a name that can differ.
    /// </remarks>
    public const string HoldUntilAskedField = "hold";

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

        // Said differently for a held submission, where "we'll pass the new version on" would be a
        // promise about something nobody has asked for yet.
        if (request.HasSharedBefore)
            body.Append(request.HoldUntilAsked
                ? """
                  <p>This is what you wrote last time. Change it and send it again, and the new
                  version replaces it.</p>
                  """
                : """
                  <p>This is what you shared last time. Change it and send it again, and we'll pass
                  the new version on.</p>
                  """);

        body.Append(
            $"""
             <form method="post" enctype="multipart/form-data" action="{HttpUtility.HtmlAttributeEncode(action)}">
               <textarea name="{IdeasField}" rows="10" maxlength="{GiftIdeaContentPolicy.MaxLength}" style="box-sizing:border-box;width:100%;padding:12px;border:1px solid #cccccc;border-radius:4px;font:inherit;">{HttpUtility.HtmlEncode(request.Ideas)}</textarea>
             """);

        // Only on the participant's own path. Somebody answering an ask has been asked by
        // definition, so offering to wait for one would be offering nothing.
        if (!route.IsContribution)
            body.Append(ComposeHoldChoice(request));

        body.Append(
            """
               <p style="margin:20px 0 0;">
                 <button type="submit" style="background-color:#1f7a4d;color:#ffffff;padding:12px 22px;border:0;border-radius:4px;font-weight:bold;font-size:16px;cursor:pointer;">
                   Share
                 </button>
               </p>
             </form>
             """);

        body.Append(
            $"""
             <p style="color:#666666;font-size:14px;">Up to {GiftIdeaContentPolicy.MaxLength:N0} characters.
             Links are welcome &mdash; paste the full web address rather than a shortened one. Please
             don't mention the name of the person you drew.</p>
             """);

        return Page(
            route.IsContribution ? $"Gift ideas for {route.Subject.Name}" : "Share your gift ideas",
            body.ToString());
    }

    /// <summary>
    /// The choice to hold a submission back, and what holding it back means.
    /// </summary>
    /// <remarks>
    /// Unticked unless what is already stored was held, so the box reflects the standing choice
    /// rather than resetting on every visit. It also survives a refusal, which is where it matters
    /// most: a box that quietly untucked itself would turn the corrected retry into a send.
    ///
    /// The promise underneath is the plainest true statement available, and the caveat after it is
    /// what keeps it true. Somebody who has already shared outright once cannot unsend that, and a
    /// page telling them nothing will ever be seen would be describing a different situation.
    /// </remarks>
    private static string ComposeHoldChoice(ComposeShareIdeasFormRequest request)
    {
        var ticked = request.HoldUntilAsked ? " checked" : string.Empty;

        var caveat = request.HasSharedOutrightBefore
            ? """
              <p style="margin:8px 0 0;color:#666666;font-size:14px;">You've already shared ideas
              once, and we can't take those back &mdash; this applies to what you send from here
              on.</p>
              """
            : string.Empty;

        return $"""
                  <p style="margin:16px 0 0;">
                    <label for="{HoldUntilAskedField}" style="display:block;padding:10px 12px;background-color:#faf8f5;border-radius:4px;cursor:pointer;">
                      <input type="checkbox" id="{HoldUntilAskedField}" name="{HoldUntilAskedField}" value="yes"{ticked} style="margin-right:10px;" />
                      Only share if the person who has my name asks
                    </label>
                  </p>
                  <p style="margin:8px 0 0;color:#666666;font-size:14px;">If the person who picked
                  your name doesn't ask for gift ideas, these will never be seen by anyone.</p>
                  {caveat}
                """;
    }

    /// <summary>
    /// What was submitted, word for word.
    /// </summary>
    /// <remarks>
    /// The echo that used to arrive by email. Nothing about the text is guessed at any more, but
    /// seeing exactly what went is still the reassurance somebody wants after pressing a button.
    ///
    /// A held submission gets the same words whether it is still sitting here or has just been sent
    /// because the person who drew them had already asked. That is deliberate: the difference between
    /// the two is the fact that somebody asked, and telling the writer that would undo the whole
    /// point of being able to ask other people instead of the recipient. What the page says is true
    /// in both cases &mdash; these go to that one person, and only if they ask.
    /// </remarks>
    internal string ComposeShared(ComposeSharedIdeasRequest request)
    {
        var route = request.Route;
        var encodedSubject = HttpUtility.HtmlEncode(route.Subject.Name);

        // A contribution is never held, because the checkbox is not offered on that path. Stated
        // here as well so that a stray field on a contribution cannot make this page describe an
        // arrangement that does not exist.
        var held = request.HoldUntilAsked && !route.IsContribution;

        var intro = route.IsContribution
            ? $"<p>Thanks &mdash; your ideas are on their way to the person shopping for {encodedSubject}. They'll see that these came from you.</p>"
            : held
                ? "<p>Thanks &mdash; these are saved. We'll pass them on to the person who picked your name only if they ask for gift ideas. If they never ask, nobody will ever see them.</p>"
                : "<p>Thanks &mdash; your gift ideas are on their way to the person who picked your name.</p>";

        var closing = held
            ? """
              <p>Thought of something else? Use the same button in your email to change it, and the
              new version replaces this one.</p>
              """
            : """
              <p>Thought of something else? Use the same button in your email to change what you
              shared, and we'll send the new version.</p>
              """;

        return Page(
            held ? "Saved!" : "Shared!",
            $"""
             {intro}
             <p>Here's exactly what we've {(held ? "saved" : "sent")}:</p>
             <div style="border-left:3px solid #cccccc;padding-left:12px;color:#333333;">
             {HttpUtility.HtmlEncode(request.Ideas).Replace("\n", "<br />")}
             </div>
             {closing}
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
