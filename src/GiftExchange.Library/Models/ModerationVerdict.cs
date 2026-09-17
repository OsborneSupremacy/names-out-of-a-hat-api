namespace GiftExchange.Library.Models;

/// <summary>
/// What content moderation made of some text.
/// </summary>
/// <remarks>
/// Three outcomes rather than a pass or fail, because the two ways of failing call for opposite
/// answers. Text that was checked and refused has to be reworded; text that could not be checked
/// may be perfectly fine, and telling its author to reword it would be both wrong and useless.
/// Both are refusals all the same: nothing unchecked is passed on.
/// </remarks>
public enum ModerationVerdict
{
    /// <summary>Checked, and nothing scored at or above the threshold. Also empty text.</summary>
    Clean,

    /// <summary>Checked, and at least one passage scored at or above the threshold.</summary>
    Toxic,

    /// <summary>Comprehend could not be reached, so the text was not checked.</summary>
    Unavailable
}
