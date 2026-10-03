namespace GiftExchange.Library.Messaging;

/// <summary>
/// What <c>GiftExchangeProvider.GetParticipantViewAsync</c> found: the exchange, and whether the
/// caller may see it at all.
/// </summary>
/// <remarks>
/// The view carries every pick in the exchange. Which of them leave is
/// <c>GetParticipantViewService</c>'s decision, and this is the read it decides about.
/// </remarks>
internal record GetParticipantViewDataResponse
{
    public required bool Exists { get; init; }

    public required ParticipantView View { get; init; }
}
