using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// The pages somebody sees after clicking SHARE IDEAS ABOUT SOMEONE ELSE.
/// </summary>
/// <remarks>
/// Hand-written HTML with inline styles and no scripts, for the reason <see cref="AskPageComposer"/>
/// gives. The shell around each page is <see cref="EmailLinkedPage"/>.
///
/// One page does the whole job: who the ideas are about and what they are are asked together, and
/// one button sends both. Splitting it — choose, then write — would mean a page rendered by a POST,
/// which nothing here does, and would make a refresh re-post what was already sent.
///
/// <b>No small-exchange warning</b>, unlike <see cref="AskPageComposer"/>, and the omission is
/// deliberate rather than an oversight. That warning exists because whoever is asked can work out
/// who asked them. Here nobody is put in that position: the reader of the email already knows they
/// drew the subject and learns only who wrote about them, the person writing learns nothing at all,
/// and the subject is never told. There is no deduction to warn anybody about, so please do not add
/// one by copying the Ask's block across.
///
/// What this page must never do is say where the ideas went, or whether they went. See
/// <see cref="ComposeShared"/>.
/// </remarks>
[UsedImplicitly]
public class OfferIdeasPageComposer
{
    private const string OfferIdeasUrl = "https://api.namesoutofahat.com/offer";

    /// <summary>
    /// The field the chosen subject is posted under.
    /// </summary>
    /// <remarks>
    /// Public for the reason <see cref="ShareIdeasPageComposer.IdeasField"/> is: the parser and the
    /// tests both build bodies from it, and a name spelled twice is a name that can differ.
    /// </remarks>
    public const string SubjectField = "about";

    /// <summary>
    /// The form: who the ideas are about, a box to write in, and who will see what is written.
    /// </summary>
    /// <remarks>
    /// Radio buttons rather than the Ask's checkboxes, because one paragraph cannot be about three
    /// people. Nothing is ticked on the way in: the Ask offers the reader's own pick first because
    /// asking them is the ordinary case, and there is no equivalent here — every name is as likely
    /// as any other, so an untouched form is a slip and is answered as one.
    ///
    /// Says the ideas will be attributed before the box rather than after the button, as the share
    /// page does, and for the same reason: somebody who would rather stay out of it is entitled to
    /// know before they write, not afterwards.
    ///
    /// The chosen name and the text both survive a refusal, so correcting a rejected submission is
    /// an edit rather than starting again.
    /// </remarks>
    internal string ComposeForm(ComposeOfferIdeasFormRequest request)
    {
        var action = $"{OfferIdeasUrl}/{HttpUtility.UrlEncode(request.Token)}";

        var body = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(request.Notice))
            body.Append(
                $"""
                 <p style="margin:0 0 20px;padding:12px 16px;background-color:#fdf3d8;border-radius:4px;">{request.Notice}</p>
                 """);

        body.Append(
            """
            <p>If you know what somebody in the exchange would like, we'll pass your ideas to
            whoever is shopping for them. <b>They'll see the ideas came from you.</b> We won't tell
            you who they are, and we won't tell the person your ideas are about.</p>
            """);

        body.Append(
            $"""
             <form method="post" enctype="multipart/form-data" action="{HttpUtility.HtmlAttributeEncode(action)}">
               <p style="margin:0 0 8px;font-weight:bold;">Who are these ideas about?</p>
             """);

        foreach (var candidate in request.Candidates)
        {
            var id = $"{SubjectField}-{candidate.ParticipantId}";
            var ticked = candidate.ParticipantId == request.ChosenSubjectId ? " checked" : string.Empty;

            body.Append(
                $"""
                 <label for="{HttpUtility.HtmlAttributeEncode(id)}" style="display:block;margin:0 0 6px;padding:10px 12px;background-color:#faf8f5;border-radius:4px;cursor:pointer;">
                   <input type="radio" id="{HttpUtility.HtmlAttributeEncode(id)}" name="{SubjectField}" value="{HttpUtility.HtmlAttributeEncode(candidate.ParticipantId.ToString())}"{ticked} style="margin-right:10px;" />
                   {HttpUtility.HtmlEncode(candidate.Name)}
                 </label>
                 """);
        }

        // Said once, under the list, rather than against the missing name. Spelling out that the
        // reader's own pick has been left out would name them on a page somebody may be reading
        // over a shoulder, and the reader is the one person who does not need telling.
        body.Append(
            """
            <p style="margin:8px 0 20px;color:#666666;font-size:14px;">The person whose name you
            drew isn't on this list &mdash; use the SHARE GIFT IDEAS button in your email to send
            them something instead.</p>
            """);

        body.Append(
            $"""
               <p style="margin:0 0 8px;font-weight:bold;">What do you think they'd like?</p>
               <textarea name="{ShareIdeasPageComposer.IdeasField}" rows="10" maxlength="{GiftIdeaContentPolicy.MaxLength}" style="box-sizing:border-box;width:100%;padding:12px;border:1px solid #cccccc;border-radius:4px;font:inherit;">{HttpUtility.HtmlEncode(request.Ideas)}</textarea>
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

        return Page("Share ideas about someone else", body.ToString());
    }

    /// <summary>
    /// What was submitted, word for word.
    /// </summary>
    /// <remarks>
    /// <b>Every word here has to be true of three different outcomes</b>, because the reader must
    /// not be able to tell which of them happened: the ideas were sent; nobody holds the subject's
    /// name, so there was nobody to send them to; or the person holding it turned out to be the
    /// reader themselves. Anything that varied between the three would let somebody learn the whole
    /// draw by offering ideas about each participant in turn.
    ///
    /// So this says what the arrangement is and never that anything has been sent. "On their way"
    /// and "we've sent these" are both lies in two cases out of three, and lies that give the game
    /// away. <see cref="ShareIdeasPageComposer.ComposeShared"/> solves the same problem the same way
    /// for a held submission.
    ///
    /// Echoing the text back gives nothing away: the reader wrote it.
    /// </remarks>
    internal string ComposeShared(ComposeOfferedIdeasRequest request)
    {
        var encodedSubject = HttpUtility.HtmlEncode(request.SubjectName);

        return Page(
            "Thanks!",
            $"""
             <p>Here's exactly what you wrote about {encodedSubject}:</p>
             <div style="border-left:3px solid #cccccc;padding-left:12px;color:#333333;">
             {HttpUtility.HtmlEncode(request.Ideas).Replace("\n", "<br />")}
             </div>
             <p>These go to the person shopping for {encodedSubject} and to nobody else &mdash; not
             {encodedSubject}, and not the organizer. They'll see the ideas came from you. We won't
             tell you who they are.</p>
             <p>You can close this page.</p>
             """);
    }

    /// <summary>
    /// What somebody sees when they have already written about this person recently.
    /// </summary>
    /// <remarks>
    /// Names the date, for the reason <see cref="GiftIdeaEmailCompositionService.ComposeAskSummary"/>
    /// gives: the likeliest reader is somebody who does not remember, and a date turns "you already
    /// did this" from an accusation into a fact they can check.
    ///
    /// Telling them this breaks nothing. What it reveals is their own sharing history, which is
    /// theirs already — unlike <see cref="ComposeShared"/>, which must not vary, because what varies
    /// there is a fact about the draw.
    ///
    /// Degrades to "recently" when the throttle could not read a timestamp back, rather than
    /// printing a date it does not have.
    /// </remarks>
    internal string ComposeAlreadyShared(string subjectName, DateTimeOffset previouslySharedAt)
    {
        var encodedSubject = HttpUtility.HtmlEncode(subjectName);

        var when = previouslySharedAt == DateTimeOffset.MinValue
            ? "recently"
            : $"on {previouslySharedAt:d MMMM yyyy}";

        return Page(
            "You've already shared these",
            $"""
             <p>You shared ideas about {encodedSubject} {when}, and we haven't passed this on.</p>
             <p>You can share ideas about {encodedSubject} again in a week. There's no limit on
             writing about somebody else in the meantime &mdash; use the same button in your
             email.</p>
             """);
    }

    private static string Page(string heading, string body) =>
        EmailLinkedPage.Compose(heading, body);
}
