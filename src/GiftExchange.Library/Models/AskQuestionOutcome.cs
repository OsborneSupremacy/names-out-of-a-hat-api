namespace GiftExchange.Library.Models;

/// <summary>
/// Whether the question typed on the Ask page may go out with the asks.
///
/// Everything other than <see cref="Accepted"/> is a refusal the page explains, with the question and
/// the ticked names kept in the form so it can be fixed and sent again.
/// </summary>
public enum AskQuestionOutcome
{
    /// <summary>Fine to send, including when there is no question at all.</summary>
    Accepted,

    /// <summary>Longer than <see cref="Services.AskQuestionPolicy.MaxLength"/>.</summary>
    RejectedTooLong,

    /// <summary>
    /// The question contains the asker's own name, which would tell everyone reading it who asked.
    /// </summary>
    RejectedWouldRevealAsker,

    /// <summary>Anything that reads as a link, which no question needs.</summary>
    RejectedContainsLink,

    /// <summary>Content moderation refused it.</summary>
    RejectedInappropriateContent,

    /// <summary>Content moderation could not be reached, and unchecked text is not sent.</summary>
    RejectedModerationUnavailable
}
