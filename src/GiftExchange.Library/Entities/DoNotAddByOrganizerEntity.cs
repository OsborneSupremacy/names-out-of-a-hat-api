namespace GiftExchange.Library.Entities;

/// <summary>
/// An address that must never be added to any gift exchange run by one particular organizer.
///
/// The middle of the three lists, offered on the leave page and recorded only if it is asked for.
/// It is what somebody reaches for when the problem is the person rather than the occasion.
/// </summary>
public class DoNotAddByOrganizerEntity
{
    public required Guid DoNotAddByOrganizerId { get; set; }

    /// <summary>
    /// The organizer being refused, as a mailbox key (see <c>ToMailboxKey</c>), so that a refusal
    /// of <c>me+1@</c> is not escaped by becoming <c>me+2@</c>.
    /// </summary>
    /// <remarks>
    /// An address rather than a <see cref="PersonEntity.PersonId"/>, deliberately. The address is
    /// already this application's unique identity for a person, and holding it means the check
    /// needs no person lookup before it can run — which is what allows all three lists to be
    /// consulted concurrently from an organizer email the request already carries.
    /// </remarks>
    public required string OrganizerEmailNormalized { get; set; }

    /// <summary>The refusing address, lower-cased and trimmed.</summary>
    public required string EmailNormalized { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
