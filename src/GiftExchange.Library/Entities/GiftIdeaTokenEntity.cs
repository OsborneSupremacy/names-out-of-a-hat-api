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
    /// The participant this token writes for. Not unique: an Ask and the invitation reminder each
    /// issue another alongside the ones already live.
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

    /// <summary>
    /// Whether a button pressed with this token proves its holder has seen their invitation.
    /// </summary>
    /// <remarks>
    /// True for a token carried by an invitation, and for the one behind the link to the invitation
    /// page: the pages either opens show the pick, so a person pressing a button on them has seen
    /// it. False for a token issued by an Ask. That email may be the first thing from this
    /// application its reader has seen at all — which is exactly the reader the reminder is for — so
    /// sharing ideas from it says nothing about the invitation.
    ///
    /// Non-nullable here and nullable in the database, for the reason
    /// <see cref="HatEntity.CopiedFromHatId"/> gives. gift_idea_token--0004 filled in the rows that
    /// existed.
    /// </remarks>
    public required bool ProvesInvitationSeen { get; set; }

    /// <summary>
    /// When a button was first pressed on a page this token opened, or
    /// <see cref="DateTimeOffset.MinValue"/> until one has been.
    /// </summary>
    /// <remarks>
    /// Only a POST sets it. Mail scanners fetch every link in a delivered email, so a GET says
    /// nothing about whether a person was there; pressing a button does.
    ///
    /// Non-nullable here and nullable in the database, as <see cref="ProvesInvitationSeen"/> is.
    /// </remarks>
    public required DateTimeOffset FirstUsedAt { get; set; }
}
