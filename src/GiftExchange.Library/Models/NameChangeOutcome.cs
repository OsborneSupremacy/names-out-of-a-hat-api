namespace GiftExchange.Library.Models;

/// <summary>
/// What became of an attempt to change the name somebody goes by.
/// </summary>
/// <remarks>
/// A name is stored on the person and read back into every exchange they appear in. Nothing about
/// the name itself is refused — two people in one exchange may share one — so the failures are
/// about the person: they cannot be found, or the rename is not the caller's to make.
/// </remarks>
public enum NameChangeOutcome
{
    /// <summary>They now go by the new name, everywhere they appear.</summary>
    Changed,

    /// <summary>The application has never heard of the address given.</summary>
    PersonNotFound,

    /// <summary>
    /// The caller neither is this person nor introduced them, so the name is not theirs to change.
    /// </summary>
    /// <remarks>
    /// The refusal a shared participant needs. Two organizers can have the same person in their
    /// exchanges, and without this the second could rename them in the first's — repeatedly, and
    /// invisibly to everybody but the person themselves.
    /// </remarks>
    NotTheirNameToChange
}
