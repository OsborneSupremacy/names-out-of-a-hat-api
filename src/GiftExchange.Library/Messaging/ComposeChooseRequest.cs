namespace GiftExchange.Library.Messaging;

/// <summary>
/// Everything the Ask page's form needs, on the way in and after a refused submission.
/// </summary>
/// <remarks>
/// A record rather than a parameter list for the reason <see cref="ComposeShareIdeasFormRequest"/>
/// gives: <see cref="Notice"/> and <see cref="Question"/> are both strings, one placed as markup and
/// one encoded first, and positional arguments are how the two eventually get swapped.
/// </remarks>
internal record ComposeChooseRequest
{
    /// <summary>Who the ideas are wanted about: the asker's own pick.</summary>
    public required string SubjectName { get; init; }

    /// <summary>Everyone the asker could choose, their pick included.</summary>
    public required ImmutableList<AskCandidate> Candidates { get; init; }

    /// <summary>The token from the link, which the form posts back to.</summary>
    public required string AskToken { get; init; }

    /// <summary>
    /// Shown above the form after a refused submission, and empty otherwise. This application's own
    /// words, placed as markup, so nothing a participant typed may go here.
    /// </summary>
    public required string Notice { get; init; }

    /// <summary>
    /// What goes in the question box: empty on the way in, or what they just tried to send. Written
    /// by a participant, so it is encoded before it is placed.
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// Who was ticked when the form came back. Empty means the page's own default, with only the
    /// pick ticked.
    /// </summary>
    /// <remarks>
    /// Carried through a refusal because somebody who ticked five names and then had their question
    /// sent back should only have to fix the question.
    /// </remarks>
    public required ImmutableHashSet<Guid> Chosen { get; init; }
}
