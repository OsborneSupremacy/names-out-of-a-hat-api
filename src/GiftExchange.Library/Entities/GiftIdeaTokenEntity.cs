namespace GiftExchange.Library.Entities;

/// <summary>
/// The token routing one participant's gift ideas link to their row, held as a hash.
///
/// Its own table rather than a column on <see cref="ParticipantEntity"/>, on two counts. Whoever
/// holds the plaintext can write to the exchange, which makes it a credential, and a credential has
/// no business in the row every organizer-facing query selects. And DSQL cannot ALTER COLUMN, so a
/// column added to a table that already holds rows can be neither defaulted nor tightened to NOT
/// NULL afterwards — a new table is the only way this arrives non-nullable.
/// </summary>
public class GiftIdeaTokenEntity
{
    public required Guid GiftIdeaTokenId { get; set; }

    /// <summary>
    /// The participant this token writes for. One live token each, which
    /// <c>uq_gift_idea_token_participant</c> enforces.
    /// </summary>
    /// <remarks>No navigation property, for the reason given on <see cref="GiftIdeaEntity.ParticipantId"/>.</remarks>
    public required Guid ParticipantId { get; set; }

    /// <summary>
    /// Hex-encoded SHA-256 of the token. Only ever the hash, as <c>LoginTokenProvider</c> keeps
    /// only the hash of a magic link token: a link is matched by hashing the token in it and
    /// looking for that, so a dump of this table lets nobody submit anything.
    /// </summary>
    public required string TokenHash { get; set; }

    public required DateTimeOffset IssuedAt { get; set; }
}
