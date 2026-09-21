namespace GiftExchange.Library.Messaging;

/// <summary>
/// What the page says after an offer has been accepted.
/// </summary>
/// <remarks>
/// Deliberately thin. Nothing about where the ideas went, or whether they went at all, may reach
/// this page: the same words are shown whether the mail was sent, whether nobody holds the
/// subject's name, and whether the recipient turned out to be the sharer themselves. Anything that
/// varied between those three would let somebody learn the draw by offering ideas about each
/// participant in turn.
/// </remarks>
internal record ComposeOfferedIdeasRequest
{
    /// <summary>Who the ideas were about. The sharer chose them, so naming them back gives nothing away.</summary>
    public required string SubjectName { get; init; }

    /// <summary>Echoed back as written. Their own words, so there is nothing here they do not have.</summary>
    public required string Ideas { get; init; }
}
