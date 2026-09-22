namespace GiftExchange.Library.Entities;

/// <summary>
/// The rows that stand for "no record".
///
/// The application already spells absence as a value rather than a null — the all-zero
/// <see cref="Guid"/> for an id, <see cref="DateTimeOffset.MinValue"/> for a date, the empty string
/// for text. These are the same idea carried one step further: a real row at the all-zero id, so
/// that a column pointing at "nobody" points at something that exists. A join to it is an inner
/// join, and the result is an empty name rather than a missing row to account for.
///
/// They are seeded twice over, and deliberately: by Liquibase into the real database
/// (the --0002 seed files in db/tables), and by <c>HasData</c> into the model, which is
/// what puts them in the databases the test suite builds with EnsureCreated. NoRecordTests holds
/// the two spellings to each other.
///
/// Methods rather than properties, because entities are mutable: a shared instance would be one
/// stray assignment away from a sentinel that is no longer empty.
/// </summary>
public static class NoRecord
{
    /// <summary>Nobody. Referenced by <see cref="Hat"/>, and by any person id that is not set.</summary>
    public static PersonEntity Person() =>
        new()
        {
            PersonId = Guid.Empty,
            Name = string.Empty,
            Email = string.Empty,
            // Its own id, which is what every person who was introduced by nobody holds. The
            // sentinel introduced itself in the only sense available to it.
            AddedByPersonId = Guid.Empty
        };

    /// <summary>
    /// No gift exchange. Its organizer is <see cref="Person"/>, so the sentinel is self-consistent:
    /// following it leads to the other sentinel rather than off the end of the table.
    /// </summary>
    public static HatEntity Hat() =>
        new()
        {
            HatId = Guid.Empty,
            OrganizerPersonId = Guid.Empty,
            Name = string.Empty,
            NameNormalized = string.Empty,
            Status = string.Empty,
            StatusUpdatedAt = DateTimeOffset.MinValue,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            InvitationsQueuedAt = DateTimeOffset.MinValue,
            InvitationsSentFromIp = string.Empty,
            CreatedAt = DateTimeOffset.MinValue,
            CopiedFromHatId = Guid.Empty,
            ExchangeDate = DateOnly.MinValue,
            ClosePromptSentAt = DateTimeOffset.MinValue
        };

    /// <summary>
    /// Nobody taking part in nothing. It sits in <see cref="Hat"/> as <see cref="Person"/>, and
    /// draws itself — so picked_recipient_participant_id, the one column that really does hold the
    /// all-zero id in normal operation, resolves to a row for every participant in the table.
    /// </summary>
    public static ParticipantEntity Participant() =>
        new()
        {
            ParticipantId = Guid.Empty,
            HatId = Guid.Empty,
            PersonId = Guid.Empty,
            PickedRecipientParticipantId = Guid.Empty,
            Emoji = string.Empty
        };
}
