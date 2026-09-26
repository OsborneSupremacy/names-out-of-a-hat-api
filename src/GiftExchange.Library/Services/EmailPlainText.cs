using MimeKit.Text;

namespace GiftExchange.Library.Services;

/// <summary>
/// The plain-text part every outgoing email carries alongside its HTML.
/// </summary>
/// <remarks>
/// Derived from the HTML rather than composed alongside it. There are a dozen composers, and a
/// second hand-written version of each would drift from the first the first time somebody reworded
/// a sentence in one and not the other — and the text part is the one nobody looks at, so nobody
/// would notice.
///
/// It exists for the filters more than for the readers. A message that is HTML alone is one of the
/// signals spam filters weigh against a sender, and most of the people receiving these have never
/// heard of this application and did not ask to hear from it. A reader who does see it, in a
/// text-only client or a screen reader that prefers the plain part, gets every sentence and every
/// link.
///
/// Built on MimeKit's tokenizer rather than on regular expressions, so that an attribute containing
/// a <c>&gt;</c> or an entity in a name is handled by something that knows HTML. The markup these
/// composers produce is simple — line breaks, links styled as buttons, one small table — and this
/// handles that and nothing more ambitious.
/// </remarks>
internal static class EmailPlainText
{
    internal static string FromHtml(string html)
    {
        var output = new StringBuilder();

        // The text inside the link currently open, held until the link closes so the address can
        // be written after it. Null outside a link.
        StringBuilder? linkText = null;
        var linkHref = string.Empty;

        // Head, style and script content is not text anybody was meant to read.
        var skipping = 0;

        using var reader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(reader);

        while (tokenizer.ReadNextToken(out var token))
        {
            switch (token)
            {
                case HtmlDataToken data when skipping == 0:
                    (linkText ?? output).Append(CollapseWhitespace(data.Data));
                    break;

                case HtmlTagToken tag when IsSkipped(tag.Id):
                    if (tag.IsEndTag)
                        skipping = Math.Max(0, skipping - 1);
                    else if (!tag.IsEmptyElement)
                        skipping++;
                    break;

                case HtmlTagToken { Id: HtmlTagId.A } tag when !tag.IsEndTag:
                    linkText = new StringBuilder();
                    linkHref = AttributeValue(tag, HtmlAttributeId.Href);
                    break;

                case HtmlTagToken { Id: HtmlTagId.A, IsEndTag: true }:
                    if (linkText is not null)
                        output.Append(DescribeLink(linkText.ToString(), linkHref));
                    linkText = null;
                    break;

                case HtmlTagToken { Id: HtmlTagId.Image } tag:
                    (linkText ?? output).Append(AttributeValue(tag, HtmlAttributeId.Alt));
                    break;

                case HtmlTagToken { Id: HtmlTagId.Br }:
                    (linkText ?? output).Append('\n');
                    break;

                // A cell boundary is a space, so the names in a row of the completion table stay
                // on one line and apart from one another.
                case HtmlTagToken { Id: HtmlTagId.TD, IsEndTag: true }:
                    output.Append(' ');
                    break;

                case HtmlTagToken tag when tag.IsEndTag && IsBlock(tag.Id):
                    output.Append('\n');
                    break;
            }
        }

        return Tidy(output.ToString());
    }

    private static bool IsSkipped(HtmlTagId id) =>
        id is HtmlTagId.Head or HtmlTagId.Style or HtmlTagId.Script or HtmlTagId.Title;

    private static bool IsBlock(HtmlTagId id) =>
        id is HtmlTagId.P or HtmlTagId.Div or HtmlTagId.TR or HtmlTagId.Table or HtmlTagId.LI
            or HtmlTagId.H1 or HtmlTagId.H2 or HtmlTagId.H3 or HtmlTagId.H4 or HtmlTagId.H5 or HtmlTagId.H6;

    private static string AttributeValue(HtmlTagToken tag, HtmlAttributeId id) =>
        tag.Attributes.FirstOrDefault(attribute => attribute.Id == id)?.Value ?? string.Empty;

    /// <summary>
    /// A link as a reader of plain text needs it: the words, then where they go.
    /// </summary>
    /// <remarks>
    /// Always both, when they differ. Every link in these emails is the thing the email exists to
    /// get somebody to — a button to share ideas, the way out of an exchange — and in plain text a
    /// link with its address dropped is a sentence that promises something and cannot deliver it.
    /// </remarks>
    private static string DescribeLink(string text, string href)
    {
        var words = text.Trim();

        var address = href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? href["mailto:".Length..]
            : href;

        if (string.IsNullOrWhiteSpace(address) || words.Contains(address, StringComparison.OrdinalIgnoreCase))
            return words;

        return string.IsNullOrEmpty(words) ? address : $"{words} ({address})";
    }

    /// <summary>
    /// Whitespace in HTML source is layout, not content: a run of it is one space.
    /// </summary>
    private static string CollapseWhitespace(string text)
    {
        var collapsed = new StringBuilder(text.Length);
        var previousWasSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                    collapsed.Append(' ');
                previousWasSpace = true;
            }
            else
            {
                collapsed.Append(character);
                previousWasSpace = false;
            }
        }

        return collapsed.ToString();
    }

    /// <summary>
    /// Trims every line and allows at most one blank line in a row.
    /// </summary>
    /// <remarks>
    /// The composers separate paragraphs with a pair of line breaks, and the whitespace between tags
    /// in their source becomes stray spaces at the ends of lines. Both are right for the HTML and
    /// untidy here.
    /// </remarks>
    private static string Tidy(string text)
    {
        var lines = text.Split('\n').Select(line => line.Trim());

        var tidied = new StringBuilder();
        var blankRun = 0;

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blankRun++;
                if (blankRun > 1 || tidied.Length == 0)
                    continue;
            }
            else
            {
                blankRun = 0;
            }

            tidied.Append(line).Append('\n');
        }

        return tidied.ToString().TrimEnd() + "\n";
    }
}
