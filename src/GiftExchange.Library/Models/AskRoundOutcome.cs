namespace GiftExchange.Library.Models;

/// <summary>
/// How far a round of asking for gift ideas got.
/// </summary>
/// <remarks>
/// Only <see cref="Asked"/> has asked anybody, and even then not necessarily everybody: which of the
/// chosen people were asked, and which the throttle held back, is in the attempts that come with it.
/// The two refusals cost the asker nothing — no throttle slot claimed and nothing released.
/// </remarks>
public enum AskRoundOutcome
{
    /// <summary>Every chosen person was either asked or refused by the throttle.</summary>
    Asked,

    /// <summary>Nobody that this asker may ask was chosen.</summary>
    NobodyChosen,

    /// <summary>The question could not go out. Nobody was asked.</summary>
    QuestionRefused
}
