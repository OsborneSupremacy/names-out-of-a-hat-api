namespace GiftExchange.Library.Services;

/// <summary>
/// The rules the optional question on the Ask page has to satisfy before it goes out, other than
/// moderation.
/// </summary>
/// <remarks>
/// Stricter than <see cref="GiftIdeaContentPolicy"/> about links, and for a reason that policy does
/// not have. Shared ideas go to somebody who asked for them; a question goes to people who asked for
/// nothing, and there is no question worth asking that needs a link in it.
///
/// The name rule points the other way from the share page's. There, the danger is the writer naming
/// the person they drew; here the readers already know who the ask is about, and the thing that must
/// not reach them is who is asking.
/// </remarks>
[UsedImplicitly]
internal class AskQuestionPolicy
{
    /// <summary>
    /// The longest question accepted, in UTF-16 code units, which is what both
    /// <see cref="string.Length"/> and a textarea's <c>maxlength</c> count.
    /// </summary>
    /// <remarks>
    /// Room for a question or two — a size, a colour, whether they already own the thing — without
    /// inviting a letter, which would read less like a question and more like a way to be recognised.
    ///
    /// The Ask form posts URL-encoded rather than as multipart, because it also carries a checkbox per
    /// participant and a multipart part costs far more framing than a <c>who=</c> pair does. Encoded,
    /// one code unit costs at most nine bytes, so the question is at most 2.7 KB; at the
    /// <see cref="ParticipantLimit.MaxParticipants"/> cap the checkboxes add about 2 KB more. Both
    /// together stay well inside the 8 KB request body the web application firewall allows — see
    /// <see cref="GiftIdeaContentPolicy.MaxLength"/>.
    /// </remarks>
    public const int MaxLength = 300;

    /// <summary>
    /// Applies every rule that does not need a network call.
    /// </summary>
    /// <param name="question">The question as typed, already trimmed.</param>
    /// <param name="askerName">The name of the participant asking, which must not reach anyone.</param>
    /// <returns><see cref="AskQuestionOutcome.Accepted"/> when nothing is wrong with it.</returns>
    public AskQuestionOutcome Check(string question, string askerName)
    {
        // The question is optional, and leaving it out is the ordinary case.
        if (string.IsNullOrWhiteSpace(question))
            return AskQuestionOutcome.Accepted;

        if (question.Length > MaxLength)
            return AskQuestionOutcome.RejectedTooLong;

        // The easiest way to give yourself away is to sign off, and a signature is a habit rather
        // than a decision. Same word-boundary matching, and the same knowingly-accepted false
        // positives, as the share page's pick check.
        if (GiftIdeaContentPolicy.RevealsPickedRecipient(question, askerName))
            return AskQuestionOutcome.RejectedWouldRevealAsker;

        if (!GiftIdeaContentPolicy.FindLinks(question).IsEmpty)
            return AskQuestionOutcome.RejectedContainsLink;

        return AskQuestionOutcome.Accepted;
    }
}
