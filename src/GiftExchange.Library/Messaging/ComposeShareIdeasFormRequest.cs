namespace GiftExchange.Library.Messaging;

/// <summary>
/// Everything the share page's form needs.
/// </summary>
/// <remarks>
/// A record rather than a parameter list, because two of these are strings of HTML-adjacent text
/// that differ only in whether they may be placed as markup, and positional arguments are how the
/// two eventually get swapped.
/// </remarks>
internal record ComposeShareIdeasFormRequest
{
    /// <summary>Who is writing, and whether the ideas are their own or about somebody else.</summary>
    public required GiftIdeaRoute Route { get; init; }

    /// <summary>The token from the link, which the form posts back to.</summary>
    public required string Token { get; init; }

    /// <summary>
    /// What goes in the textarea: what they last shared, or what they just tried to send. Written by
    /// a participant, so it is encoded before it is placed.
    /// </summary>
    public required string Ideas { get; init; }

    /// <summary>
    /// Shown above the form after a refused submission, and empty otherwise. This application's own
    /// words, placed as markup, so nothing a participant typed may go here.
    /// </summary>
    public required string Notice { get; init; }

    /// <summary>Whether <see cref="Ideas"/> is something already shared, rather than a draft.</summary>
    public required bool HasSharedBefore { get; init; }
}
