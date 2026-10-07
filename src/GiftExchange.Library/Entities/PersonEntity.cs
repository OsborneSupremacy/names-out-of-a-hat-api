namespace GiftExchange.Library.Entities;

/// <summary>
/// Somebody the application knows about, identified by their email address.
///
/// Organizers and participants are the same kind of thing, and both are rows in this table. A hat
/// points at the person who organizes it; a participant row points at the person taking part. Once
/// that is true, <see cref="Name"/> is stored in exactly one place, and renaming somebody is a
/// single write rather than a sweep over every hat they appear in.
///
/// One row per email address for the whole system, which is what the unique index on
/// <see cref="Email"/> enforces.
/// </summary>
public class PersonEntity
{
    public required Guid PersonId { get; set; }

    public required string Name { get; set; }

    public required string Email { get; set; }

    /// <summary>
    /// The person who introduced this one to the application, and their own id when nobody did.
    /// </summary>
    /// <remarks>
    /// This is what decides whose <see cref="Name"/> it is to change. A name is stored here and
    /// nowhere else, so an edit to one reaches every exchange the person appears in — and an
    /// organizer who shares a participant with another organizer would otherwise be able to rename
    /// them out from under both. Two people may make the edit: this person, who holds the address
    /// the row is identified by, and whoever first typed their name into an exchange. Everybody
    /// else is refused.
    ///
    /// Self-referencing rather than sentinel-valued for somebody who arrived under their own steam,
    /// which is the case for every organizer creating their first exchange. That reads correctly
    /// through the rule instead of needing an exception to it: a person may always change their own
    /// name, and "nobody introduced them" is spelled as "they introduced themselves".
    ///
    /// Never handed to another organizer. One adding somebody who is already known does not acquire
    /// their name; the person is found, and the name they already have stands.
    ///
    /// The person can take it, though. Once they set their own name this becomes their own id, and
    /// whoever introduced them can no longer change it. Who that was is given up to do it; this
    /// column answers whose name it is to change, and nothing reads it for anything else.
    ///
    /// Non-nullable here and nullable in the database, for the reason
    /// <see cref="HatEntity.CopiedFromHatId"/> gives: the column was added to a table that already
    /// had rows, and DSQL rejects both halves of the usual remedy. person--0004 filled in the rows
    /// that existed, and this property is what keeps every row written since from carrying a NULL.
    /// </remarks>
    public required Guid AddedByPersonId { get; set; }

    /// <summary>Hats this person organizes.</summary>
    public ICollection<HatEntity> OrganizedHats { get; set; } = [];

    /// <summary>Gift exchanges this person takes part in.</summary>
    public ICollection<ParticipantEntity> Participations { get; set; } = [];
}
