namespace GiftExchange.Library.Messaging;

/// <summary>
/// One gift exchange, whole, in the shape an organizer downloads it.
/// </summary>
/// <remarks>
/// A wrapper around the exchange rather than the exchange itself, so that the two facts about the
/// file — what produced it and when — travel with it. A JSON document that has been sitting in
/// somebody's downloads folder for a year cannot otherwise say which of those years it is from.
/// </remarks>
[UsedImplicitly]
public record ExportHatResponse
{
    /// <summary>
    /// The shape of this document, so that anything reading one can tell what it is looking at.
    /// Bumped when a field changes meaning or leaves; adding a field does not bump it.
    /// </summary>
    public required string FormatVersion { get; init; }

    /// <summary>When the export was taken. The exchange it describes has moved on since.</summary>
    public required DateTimeOffset ExportedAt { get; init; }

    public required ExportedHat Hat { get; init; }
}

/// <summary>
/// The exchange as stored, identifiers included.
/// </summary>
/// <remarks>
/// Ids are here on purpose. They are meaningless to anybody who cannot already read the exchange —
/// every endpoint is scoped to the organizer, so holding one buys nothing — and without them the
/// export cannot say who drew whom without relying on display names, which is the one thing a
/// snapshot should not have to do.
///
/// What is not here is <c>InvitationsSentFromIp</c>. It is the organizer's own address rather than
/// anything about the exchange, and this is a file made to be moved around.
/// </remarks>
[UsedImplicitly]
public record ExportedHat
{
    public required Guid HatId { get; init; }

    public required string Name { get; init; }

    /// <summary>One of <see cref="Models.HatStatuses.All"/> at the moment of export.</summary>
    public required string Status { get; init; }

    public required string AdditionalInformation { get; init; }

    public required string PriceRange { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// <see cref="DateTimeOffset.MinValue"/> when invitations have not been queued, which is the
    /// same absence the column holds.
    /// </summary>
    public required DateTimeOffset InvitationsQueuedAt { get; init; }

    /// <summary>
    /// The exchange this one was copied from, or the all-zero <see cref="Guid"/> when it was not a
    /// copy.
    /// </summary>
    public required Guid CopiedFromHatId { get; init; }

    /// <summary>
    /// The approximate day of the exchange, or <see cref="DateOnly.MinValue"/> when the organizer
    /// did not give one.
    /// </summary>
    public required DateOnly ExchangeDate { get; init; }

    public required ExportedPerson Organizer { get; init; }

    public required ImmutableList<ExportedParticipant> Participants { get; init; }
}

/// <summary>Somebody the exchange knows about, with the id their row is keyed by.</summary>
[UsedImplicitly]
public record ExportedPerson
{
    public required Guid PersonId { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }
}

[UsedImplicitly]
public record ExportedParticipant
{
    public required Guid ParticipantId { get; init; }

    public required ExportedPerson Person { get; init; }

    /// <summary>
    /// Who this participant drew — and the empty reference until the picks are revealed.
    /// </summary>
    /// <remarks>
    /// The exchange keeps the draw from its own organizer until they close it, which is what
    /// <c>GetHatService.RedactPickedRecipients</c> enforces for the detail view. An export is
    /// another way of asking the same question, so it answers it the same way: before
    /// <see cref="Models.HatStatus.Closed"/> this is empty, and the fact that it is empty is itself
    /// accurate — nobody, the organizer included, is meant to know yet.
    /// </remarks>
    public required ExportedParticipantReference PickedRecipient { get; init; }

    /// <summary>
    /// The face this participant is marked with wherever they are named — one of
    /// <c>PersonEmoji.All</c>. Part of the snapshot because it is stored rather than derived: an
    /// export that left it out could not be read back as the exchange the organizer was looking at.
    /// </summary>
    public required string Emoji { get; init; }

    /// <summary>Who this participant was allowed to draw.</summary>
    public required ImmutableList<ExportedParticipantReference> EligibleRecipients { get; init; }

    /// <summary>
    /// How far the last email sent to this participant is known to have got. Empty means nothing
    /// has been heard, which is not the same as not delivered.
    /// </summary>
    public required string DeliveryStatus { get; init; }

    /// <summary>
    /// Why, for the statuses that have a why. Written by a remote mail server, so it is neither
    /// moderated nor trusted — whatever renders it encodes it.
    /// </summary>
    public required string DeliveryDetail { get; init; }

    /// <summary>
    /// Which of this application's emails the status above is about — one of
    /// <see cref="Models.EmailMessageType"/>. Empty when nothing has been heard.
    /// </summary>
    public required string DeliveryMessageType { get; init; }

    /// <summary>
    /// When SES says that happened, or <see cref="DateTimeOffset.MinValue"/> when nothing has been
    /// heard. The same absence <see cref="ExportedHat.InvitationsQueuedAt"/> uses.
    /// </summary>
    public required DateTimeOffset DeliveryOccurredAt { get; init; }
}

/// <summary>
/// One participant, pointed at from another. Carries the name as well as the id so the document
/// reads without being cross-referenced, and the id as well as the name so it can be.
/// </summary>
[UsedImplicitly]
public record ExportedParticipantReference
{
    public required Guid ParticipantId { get; init; }

    public required string Name { get; init; }
}

internal static class ExportedParticipantReferences
{
    /// <summary>Nobody. What an undrawn — or an unrevealed — pick exports as.</summary>
    public static ExportedParticipantReference Empty => new()
    {
        ParticipantId = Guid.Empty,
        Name = string.Empty
    };
}
