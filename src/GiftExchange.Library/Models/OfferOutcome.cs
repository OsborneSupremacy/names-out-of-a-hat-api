namespace GiftExchange.Library.Models;

/// <summary>
/// What became of an offer of gift ideas about somebody else.
/// </summary>
/// <remarks>
/// <see cref="Offered"/> means stored, and says nothing about whether anything was sent. That is
/// deliberate and is the one property of offering worth protecting: see
/// <see cref="Services.GiftIdeaOffering"/>.
/// </remarks>
public enum OfferOutcome
{
    /// <summary>Stored, and passed on if there is somebody to pass it to.</summary>
    Offered,

    /// <summary>Nobody this participant may write about was chosen.</summary>
    NobodyChosen,

    /// <summary>What was written cannot be passed on. Nothing stored, and the week not spent.</summary>
    Refused,

    /// <summary>They offered ideas about the same person within the last week. Nothing stored.</summary>
    AlreadyOffered
}
