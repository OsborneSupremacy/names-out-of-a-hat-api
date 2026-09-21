namespace GiftExchange.Library.Models;

/// <summary>
/// Somebody a participant has chosen to offer ideas about, resolved to the one person those ideas
/// are for.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="AskTarget"/> for the unprompted direction, and shaped differently
/// for one reason: an Ask is addressed to the person it names, so its target carries their address.
/// This is not. What is written about the subject goes to whoever drew them, and the subject is
/// never written to at all — so the only address here is the giver's, and the subject appears by
/// name.
///
/// Both halves in one record rather than two lookups, because a caller holding one without the
/// other can neither send nor refuse without guessing.
/// </remarks>
public record OfferTarget
{
    /// <summary>Who the ideas are about.</summary>
    public required Guid SubjectParticipantId { get; init; }

    /// <summary>
    /// The subject's name as the giver should read it. Their address reaches the reader only when
    /// somebody else in the exchange shares that name — see <c>ParticipantNaming</c>.
    /// </summary>
    public required string SubjectName { get; init; }

    /// <summary>
    /// The participant row of whoever drew the subject, or the all-zero id when nobody holds their
    /// name — which is what a subject who has left, or who was never drawn, looks like.
    /// </summary>
    /// <remarks>
    /// Compared against the sharer before anything is sent, although as things stand it can never
    /// match. Both the candidate filter and the lookup that fills this read the same column, so a
    /// subject that is not the sharer's pick cannot resolve to the sharer as its giver. The
    /// comparison is kept because the two are separate queries that could be changed apart, and
    /// what it prevents — mail about somebody going back to the person who wrote it, carrying the
    /// name of their own pick — is worth one <c>if</c>.
    /// </remarks>
    public required Guid GiverParticipantId { get; init; }

    /// <summary>
    /// Whoever drew the subject: the single person the ideas are sent to, and the only address in
    /// this record. <see cref="Persons.Empty"/> when nobody holds the subject's name.
    /// </summary>
    /// <remarks>
    /// Never shown to the sharer and never named in anything they see. That the sharer does not
    /// learn who this is is the whole promise the page makes.
    /// </remarks>
    public required Person Giver { get; init; }
}

internal static class OfferTargets
{
    /// <summary>
    /// What a chosen subject that did not survive checking resolves to. Never rendered: the caller
    /// hands the form back rather than showing a target nobody chose.
    /// </summary>
    public static OfferTarget Empty => new()
    {
        SubjectParticipantId = Guid.Empty,
        SubjectName = string.Empty,
        GiverParticipantId = Guid.Empty,
        Giver = Persons.Empty
    };
}
