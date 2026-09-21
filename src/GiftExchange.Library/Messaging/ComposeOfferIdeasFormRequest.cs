namespace GiftExchange.Library.Messaging;

/// <summary>
/// Everything the offer form needs to render, on the way in and on the way back from a refusal.
/// </summary>
/// <remarks>
/// One record for both, because a refused submission has to come back as the same page with the
/// same answers still in it. A chosen subject that came back unticked, or text that came back
/// empty, would make correcting a typo into starting again.
/// </remarks>
internal record ComposeOfferIdeasFormRequest
{
    public required string Token { get; init; }

    public required ImmutableList<OfferCandidate> Candidates { get; init; }

    /// <summary>
    /// Whichever candidate is ticked, or <see cref="Guid.Empty"/> for none. Nothing is ticked on the
    /// way in: unlike the Ask, there is no ordinary choice to offer first, so an untouched form is a
    /// slip rather than a decision.
    /// </summary>
    public required Guid ChosenSubjectId { get; init; }

    public required string Ideas { get; init; }

    /// <summary>
    /// Shown above the form when a submission came back here. Empty on the way in, and this
    /// application's own words rather than anybody else's — it is placed as markup, so nothing a
    /// participant typed may be passed here.
    /// </summary>
    public required string Notice { get; init; }
}
