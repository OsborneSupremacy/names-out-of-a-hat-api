namespace GiftExchange.Library.Models;

/// <summary>
/// What became of one gift ideas submission from the share page.
///
/// Everything other than <see cref="Shared"/> is a refusal the page explains, with what was written
/// kept in the form so it can be fixed and sent again.
/// </summary>
public enum GiftIdeaSubmissionOutcome
{
    /// <summary>Stored, and forwarded to whoever the ideas are for.</summary>
    Shared,

    /// <summary>Nothing was written.</summary>
    RejectedNothingToShare,

    /// <summary>Longer than the application will put through moderation.</summary>
    RejectedTooLong,

    /// <summary>
    /// The text contains the name of the person the sender drew. Forwarding that would tell the
    /// recipient who the sender drew.
    /// </summary>
    RejectedWouldRevealTheirPick,

    /// <summary>A shortened link, whose destination cannot be shown to the person receiving it.</summary>
    RejectedShortenedLink,

    /// <summary>A link back to this application, which no gift idea needs and phishing does.</summary>
    RejectedSelfReferentialLink,

    /// <summary>More links than a list of gift ideas has any reason to carry.</summary>
    RejectedTooManyLinks,

    /// <summary>Content moderation refused it.</summary>
    RejectedInappropriateContent,

    /// <summary>Content moderation could not be reached, and unchecked text is not forwarded.</summary>
    RejectedModerationUnavailable
}
