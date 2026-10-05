namespace GiftExchange.Library.Messaging;

/// <summary>
/// Everything about gift ideas that one participant may see in one exchange, and what they may do
/// about it from their own page.
/// </summary>
/// <remarks>
/// Each list is the signed-in copy of something this application has already told the caller by
/// email, or would have: their own ideas, what was forwarded to them about their pick, the asks put
/// to them, and the offers they made. Nothing here is new knowledge. In particular:
///
/// <list type="bullet">
///   <item>Their pick's own ideas appear only once they would have been forwarded — shared outright,
///   or held and then asked for. A held submission nobody asked for is not shown, and neither is the
///   fact that one exists.</item>
///   <item>An ask put to the caller never says who asked, as the email never does.</item>
///   <item>Their own offers never say whether anything was sent, as the confirmation page never
///   does.</item>
///   <item>Nothing tells somebody that their giver has asked about them.</item>
/// </list>
/// </remarks>
public record ParticipantGiftIdeas
{
    /// <summary>Whether the exchange is still taking gift ideas. False once it has closed.</summary>
    public required bool CanShare { get; init; }

    /// <summary>Whether the caller can ask for gift ideas: <see cref="CanShare"/>, and they have a pick.</summary>
    public required bool CanAsk { get; init; }

    /// <summary>What the caller most recently wrote about themselves. Empty when they have not.</summary>
    public required string YourIdeas { get; init; }

    /// <summary>When they wrote it. <see cref="DateTimeOffset.MinValue"/> when they have not.</summary>
    public required DateTimeOffset YourIdeasSharedAt { get; init; }

    /// <summary>Whether what they wrote is being held until whoever drew them asks.</summary>
    public required bool HoldUntilAsked { get; init; }

    /// <summary>
    /// Whether anything of theirs has ever gone out outright, which nothing can take back. The page
    /// says so beside the hold, so that it does not promise more than it can.
    /// </summary>
    public required bool HasSharedOutrightBefore { get; init; }

    /// <summary>
    /// What the caller's pick wrote about themselves, once it has been passed to the caller. Empty
    /// otherwise.
    /// </summary>
    public required string FromYourPick { get; init; }

    /// <summary>When the pick wrote it. <see cref="DateTimeOffset.MinValue"/> when there is nothing.</summary>
    public required DateTimeOffset FromYourPickSharedAt { get; init; }

    /// <summary>
    /// What other people suggested for the caller's pick, newest first: answers to the caller's asks,
    /// and ideas offered unprompted. Each says who it came from, as the email that carried it did.
    /// </summary>
    public required ImmutableList<SuggestedGiftIdeas> AboutYourPick { get; init; }

    /// <summary>Who the caller has asked about their pick, other than the pick themselves.</summary>
    public required ImmutableList<AskedHelper> AskedAboutYourPick { get; init; }

    /// <summary>Asks put to the caller about somebody else, one per person asked about.</summary>
    public required ImmutableList<GiftIdeaAskForYou> AsksForYou { get; init; }

    /// <summary>What the caller has offered about other people, one per person, newest first.</summary>
    public required ImmutableList<YourOfferedGiftIdeas> YourOffers { get; init; }

    /// <summary>Who the caller could ask. Their pick first, marked.</summary>
    public required ImmutableList<AskCandidate> AskCandidates { get; init; }

    /// <summary>Who the caller could offer ideas about: everybody but themselves and their pick.</summary>
    public required ImmutableList<OfferCandidate> OfferCandidates { get; init; }
}

/// <summary>Somebody's suggestion about the caller's pick.</summary>
public record SuggestedGiftIdeas
{
    /// <summary>Who suggested it, as <see cref="Services.ParticipantNaming"/> names them.</summary>
    public required string From { get; init; }

    public required string Ideas { get; init; }

    public required DateTimeOffset SharedAt { get; init; }

    /// <summary>Whether it answers an ask of the caller's, rather than being offered unprompted.</summary>
    public required bool WasAskedFor { get; init; }
}

/// <summary>Somebody the caller asked about their pick.</summary>
public record AskedHelper
{
    public required string Name { get; init; }

    /// <summary>The most recent time the caller asked them.</summary>
    public required DateTimeOffset AskedAt { get; init; }

    /// <summary>Whether they have answered. What they said is in <see cref="ParticipantGiftIdeas.AboutYourPick"/>.</summary>
    public required bool HasAnswered { get; init; }
}

/// <summary>
/// Somebody asking the caller what a third participant would like. Never says who asked.
/// </summary>
public record GiftIdeaAskForYou
{
    /// <summary>The most recent ask about this person; the one an answer goes back through.</summary>
    public required Guid AskId { get; init; }

    public required string SubjectName { get; init; }

    public required DateTimeOffset AskedAt { get; init; }

    /// <summary>What the caller last said in answer. Empty when they have not answered.</summary>
    public required string YourAnswer { get; init; }

    /// <summary><see cref="DateTimeOffset.MinValue"/> when they have not answered.</summary>
    public required DateTimeOffset AnsweredAt { get; init; }
}

/// <summary>
/// What the caller most recently offered about one other participant. Says nothing about whether it
/// was sent.
/// </summary>
public record YourOfferedGiftIdeas
{
    public required string SubjectName { get; init; }

    public required string Ideas { get; init; }

    public required DateTimeOffset SharedAt { get; init; }
}

internal static class ParticipantGiftIdeasDefaults
{
    /// <summary>
    /// Before the gift ideas have been read, or for somebody with none to read. Never serialized as
    /// the answer for somebody who has some.
    /// </summary>
    public static ParticipantGiftIdeas Empty => new()
    {
        CanShare = false,
        CanAsk = false,
        YourIdeas = string.Empty,
        YourIdeasSharedAt = DateTimeOffset.MinValue,
        HoldUntilAsked = false,
        HasSharedOutrightBefore = false,
        FromYourPick = string.Empty,
        FromYourPickSharedAt = DateTimeOffset.MinValue,
        AboutYourPick = [],
        AskedAboutYourPick = [],
        AsksForYou = [],
        YourOffers = [],
        AskCandidates = [],
        OfferCandidates = []
    };
}
