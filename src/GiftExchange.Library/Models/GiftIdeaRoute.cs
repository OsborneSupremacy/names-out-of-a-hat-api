namespace GiftExchange.Library.Models;

/// <summary>
/// Everything an incoming gift ideas email needs resolved before it can be acted on, found from the
/// hash of the token in the address it was sent to.
///
/// One lookup rather than several because the handler needs all of it or none of it: who is allowed
/// to have sent this, whether the exchange is still taking submissions, and where the text goes.
///
/// Covers both kinds of submission. A participant writing about themselves and a participant
/// answering somebody's request for ideas about a third person arrive down the same pipe and face
/// the same rules, and every check between authentication and moderation is identical for the two.
/// What differs is <see cref="Subject"/> — whether the ideas are the sender's own — and
/// <see cref="AskId"/> says which case this is.
/// </summary>
public record GiftIdeaRoute
{
    /// <summary>The sender's participant row, within <see cref="HatId"/>.</summary>
    public required Guid ParticipantId { get; init; }

    public required Guid HatId { get; init; }

    public required string HatName { get; init; }

    /// <summary>One of <see cref="HatStatuses.All"/>. Submissions are only accepted for some of them.</summary>
    public required string HatStatus { get; init; }

    /// <summary>
    /// The participant sharing the ideas. Their address is what an incoming message has to have
    /// come from: the token says which row to write, and this says who is allowed to write it.
    /// </summary>
    public required Person Sender { get; init; }

    /// <summary>
    /// The person the sender drew.
    ///
    /// Serves two unrelated purposes. Their name is here to be looked for: if it appears in
    /// submitted text, the sender has quoted their own invitation into the message, and forwarding
    /// that would tell <see cref="Giver"/> who the sender drew — the one secret this application
    /// keeps. And their row is where an Ask goes by default, since the likeliest person to ask
    /// about a pick is the pick themselves.
    ///
    /// This stays the sender's own pick even on a contribution, where the ideas are about somebody
    /// else entirely. The check it feeds is about what the sender must not leak, not about who the
    /// message concerns — and the two can never collide, because the subject of a contribution was
    /// drawn by the asker, so no helper can have drawn them.
    /// </summary>
    public required Person SenderPickedRecipient { get; init; }

    /// <summary>
    /// The participant row behind <see cref="SenderPickedRecipient"/>, or the all-zero id if the
    /// hat has not been shaken. An Ask needs it to issue them a token of their own.
    /// </summary>
    public required Guid SenderPickedRecipientParticipantId { get; init; }

    /// <summary>
    /// The person the ideas are about. The sender themselves on an ordinary submission; on a
    /// contribution, the participant whose name the asker drew.
    /// </summary>
    public required Person Subject { get; init; }

    /// <summary>
    /// Whoever drew <see cref="Subject"/>, and so the single person these ideas are for.
    ///
    /// On an ordinary submission that is whoever drew the sender, and telling them the sender wrote
    /// something reveals nothing — they already know whose name they hold. On a contribution it is
    /// the participant who asked, which is the same person by a shorter route: they asked because
    /// they drew the subject. The reverse is never sent in either direction.
    /// </summary>
    public required Person Giver { get; init; }

    /// <summary>
    /// The participant row behind <see cref="Giver"/>, or the all-zero id when nobody has drawn the
    /// subject.
    /// </summary>
    /// <remarks>
    /// Here so that a submission held back until somebody asks can be matched against whether that
    /// somebody has asked, which is a fact about a pair of participants rather than about a person.
    /// On a contribution it is the asker, who is the same person by a shorter route.
    /// </remarks>
    public required Guid GiverParticipantId { get; init; }

    /// <summary>
    /// The ask this submission answers, or the all-zero id when the sender is writing about
    /// themselves and nobody asked them through this route.
    /// </summary>
    public required Guid AskId { get; init; }

    /// <summary>
    /// Whether this is somebody's suggestion about another participant rather than their own words
    /// about themselves. The two are stored in different tables and described differently in every
    /// message, so nothing downstream should infer it by comparing people.
    /// </summary>
    public bool IsContribution => AskId != Guid.Empty;
}

internal static class GiftIdeaRoutes
{
    public static GiftIdeaRoute Empty => new()
    {
        ParticipantId = Guid.Empty,
        HatId = Guid.Empty,
        HatName = string.Empty,
        HatStatus = string.Empty,
        Sender = Persons.Empty,
        SenderPickedRecipient = Persons.Empty,
        SenderPickedRecipientParticipantId = Guid.Empty,
        Subject = Persons.Empty,
        Giver = Persons.Empty,
        GiverParticipantId = Guid.Empty,
        AskId = Guid.Empty
    };
}
