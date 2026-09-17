using System.Text.RegularExpressions;

namespace GiftExchange.Library.Services;

/// <summary>
/// The rules a submission has to satisfy before it is stored and forwarded, other than moderation.
///
/// Links are allowed, which is a deliberate decision rather than an omission. A wishlist URL is the
/// single most useful thing somebody can share, and refusing links would take most of the value out
/// of this feature. What makes that acceptable is how narrow the opening is: to deliver one link to
/// one person, a sender has to have been added to an exchange by its organizer and hold a secret
/// link that was mailed to their address. This is not an open relay, and the blast radius of the
/// worst case is a single recipient who already expects to hear from them.
///
/// The rules below close the gaps that would otherwise be left, in the order they are worth having:
/// the true destination of every link is always visible, links that exist to hide a destination are
/// refused, and links back to this application — which nothing legitimate needs and phishing wants
/// — are refused too.
/// </summary>
[UsedImplicitly]
internal partial class GiftIdeaContentPolicy
{
    /// <summary>
    /// The longest submission accepted, in UTF-16 code units, which is what both
    /// <see cref="string.Length"/> and a textarea's <c>maxlength</c> count.
    /// </summary>
    /// <remarks>
    /// Set by the web application firewall in front of the API rather than by anything here. The
    /// CloudFront-managed web ACL includes the AWS common rule set, whose SizeRestrictions_BODY rule
    /// blocks any request body over 8 KB with a bare CloudFront 403 before this application sees it.
    ///
    /// The share form posts as multipart/form-data so that text travels as raw UTF-8 rather than
    /// percent-encoded, where one character can cost twelve bytes. Raw, one code unit costs at most
    /// three bytes, so 2,000 of them is at most 6 KB, leaving room for the multipart framing. That
    /// holds for emoji and for any script, not just for English.
    ///
    /// It is also plenty. Two thousand characters is a long list of gift ideas, and anybody pasting
    /// more is pasting something that is not one.
    /// </remarks>
    public const int MaxLength = 2000;

    /// <summary>More links than any list of gift ideas has a reason to carry.</summary>
    public const int MaxLinks = 10;

    /// <summary>
    /// Services whose entire purpose is to stand in front of a destination.
    ///
    /// These defeat the one mitigation that does real work here — that whoever receives the ideas
    /// can see where a link goes before following it. There is no way to show somebody where a
    /// shortened link leads, so the sender is asked for the original instead.
    /// </summary>
    private static readonly ImmutableHashSet<string> LinkShorteners =
    [
        "bit.ly", "bitly.com", "tinyurl.com", "t.co", "goo.gl", "ow.ly", "is.gd", "buff.ly",
        "rebrand.ly", "cutt.ly", "shorturl.at", "rb.gy", "t.ly", "shorte.st", "adf.ly", "lnkd.in"
    ];

    /// <summary>
    /// This application's own domains. No gift idea needs to link here, and a message that appears
    /// to come from us asking somebody to sign in is the most valuable thing an attacker could get
    /// this feature to deliver.
    /// </summary>
    private static readonly ImmutableHashSet<string> OwnDomains =
    [
        "namesoutofahat.com", "api.namesoutofahat.com", "mail.namesoutofahat.com"
    ];

    /// <summary>
    /// Applies every rule that does not need a network call, in the order that gives the sender the
    /// most useful complaint first.
    /// </summary>
    /// <param name="body">The submitted text.</param>
    /// <param name="pickedRecipientName">
    /// The name of the person the sender drew, which must not reach the person the text is
    /// forwarded to.
    /// </param>
    /// <returns><see cref="GiftIdeaSubmissionOutcome.Shared"/> when nothing is wrong with it.</returns>
    public GiftIdeaSubmissionOutcome Check(string body, string pickedRecipientName)
    {
        if (string.IsNullOrWhiteSpace(body))
            return GiftIdeaSubmissionOutcome.RejectedNothingToShare;

        if (body.Length > MaxLength)
            return GiftIdeaSubmissionOutcome.RejectedTooLong;

        // Before anything else about content: this one is not about what the sender did wrong, it
        // is about what forwarding it would do to somebody else.
        if (RevealsPickedRecipient(body, pickedRecipientName))
            return GiftIdeaSubmissionOutcome.RejectedWouldRevealTheirPick;

        var links = FindLinks(body);

        if (links.Count > MaxLinks)
            return GiftIdeaSubmissionOutcome.RejectedTooManyLinks;

        if (links.Any(link => LinkShorteners.Contains(link)))
            return GiftIdeaSubmissionOutcome.RejectedShortenedLink;

        if (links.Any(link => OwnDomains.Contains(link)))
            return GiftIdeaSubmissionOutcome.RejectedSelfReferentialLink;

        return GiftIdeaSubmissionOutcome.Shared;
    }

    /// <summary>
    /// Whether the submitted text names the person the sender drew.
    /// </summary>
    /// <remarks>
    /// Whoever reads the ideas already knows who wrote them, so a name in the text that turns out
    /// to be the writer's own pick tells them who drew whom. The likeliest way in is text pasted
    /// from the invitation, which says whose name they picked.
    ///
    /// Matched on a word boundary so that a name like "Sam" does not fire on "same". It will still
    /// refuse the occasional innocent message from somebody whose recipient shares a name with a
    /// thing they want, and that trade is made knowingly: the sender is told plainly what happened
    /// and can rephrase, whereas the failure in the other direction cannot be undone once sent.
    /// </remarks>
    internal static bool RevealsPickedRecipient(string body, string pickedRecipientName)
    {
        if (string.IsNullOrWhiteSpace(pickedRecipientName))
            return false;

        return Regex.IsMatch(
            body,
            $@"\b{Regex.Escape(pickedRecipientName.Trim())}\b",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// The host of every link in the text, lower-cased.
    /// </summary>
    /// <remarks>
    /// Hosts rather than whole URLs, because every rule above is about where a link goes rather
    /// than what it points at once it arrives.
    /// </remarks>
    internal static ImmutableList<string> FindLinks(string body) =>
    [
        .. LinkPattern()
            .Matches(body)
            .Select(match => match.Value.TrimEnd('.', ',', ')', ']', '>', '"', '\''))
            .Select(ToHost)
            .Where(host => !string.IsNullOrWhiteSpace(host))
    ];

    private static string ToHost(string candidate)
    {
        var withScheme = candidate.Contains("://", StringComparison.Ordinal)
            ? candidate
            : $"https://{candidate}";

        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri))
            return string.Empty;

        var host = uri.Host.ToLowerInvariant();

        // "www." is not part of who a host is, and treating it as though it were would mean every
        // rule below had to name both spellings and would silently miss whichever was forgotten.
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>
    /// Anything that reads as a link.
    /// </summary>
    /// <remarks>
    /// Three shapes, because people write links three ways and a mail client turns all of them into
    /// something clickable at the far end. A full URL, the bare "www." form, and a bare host with a
    /// path — the last of which is how a shortened link is almost always pasted, and missing it
    /// would leave the shortener rule looking for something it never sees.
    ///
    /// The third arm requires both an alphabetic top-level domain and a slash after it, which is
    /// what keeps it from reading "Node.js" or "3.5/5" as addresses. Being generous is otherwise
    /// the safe direction here: a false positive costs a host that no rule names, while a miss
    /// costs a link that no rule was applied to at all.
    /// </remarks>
    [GeneratedRegex(
        """(?:https?://[^\s<>"']+|www\.[^\s<>"']+|(?:[a-z0-9-]+\.)+[a-z]{2,}/[^\s<>"']*)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
