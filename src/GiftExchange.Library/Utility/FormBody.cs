using System.Web;
using MimeKit;

namespace GiftExchange.Library.Utility;

/// <summary>
/// The fields out of a posted form, whichever of the two encodings a browser used.
/// </summary>
/// <remarks>
/// One implementation rather than one per handler. The pages behind the email links post
/// multipart/form-data, for the size reason <see cref="GiftIdeaContentPolicy.MaxLength"/> gives, and
/// a URL-encoded body is read too since that is what a form without an enctype sends and there is no
/// reason to refuse one.
///
/// Read once into a dictionary rather than parsed per field. A multipart parse is expensive enough
/// not to want twice, and a form asking two questions would otherwise pay for it twice.
///
/// An unreadable body is an empty one. Every caller then reports it through its own content policy,
/// which already has words for a submission that said nothing — better than a page about MIME.
/// </remarks>
internal sealed class FormBody
{
    private readonly ILookup<string, string> _fields;

    private FormBody(ILookup<string, string> fields) => _fields = fields;

    /// <summary>A body that said nothing. What an unreadable one is treated as.</summary>
    private static FormBody Nothing =>
        new(Array.Empty<KeyValuePair<string, string>>().ToLookup(entry => entry.Key, entry => entry.Value));

    /// <summary>
    /// The first value posted under a name, or the empty string when there was none.
    /// </summary>
    /// <remarks>
    /// First rather than only. A hand-made post can repeat a field, and taking the first is a
    /// definite answer where taking "the" value would have to decide what to do about two.
    /// </remarks>
    public string First(string name) => _fields[name].FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// Whether a field was posted at all, whatever it carried.
    /// </summary>
    /// <remarks>
    /// What a checkbox is read by: an unticked box is not posted, and its value when ticked is a
    /// browser default rather than a contract.
    /// </remarks>
    public bool Has(string name) => _fields[name].Any();

    /// <summary>Every value posted under a name, in the order they arrived.</summary>
    public ImmutableList<string> All(string name) => [.. _fields[name]];

    public static FormBody Read(APIGatewayProxyRequest request)
    {
        byte[] body;

        try
        {
            body = request.IsBase64Encoded
                ? Convert.FromBase64String(request.Body ?? string.Empty)
                : Encoding.UTF8.GetBytes(request.Body ?? string.Empty);
        }
        catch (FormatException)
        {
            return Nothing;
        }

        var contentType = FindHeader(request, "Content-Type");

        return contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            ? ReadMultipart(body, contentType)
            : ReadUrlEncoded(body);
    }

    /// <summary>
    /// The fields out of a multipart/form-data body.
    /// </summary>
    /// <remarks>
    /// MimeKit is already here for sending mail, and a form post is a MIME multipart with a
    /// Content-Disposition on each part. The request's Content-Type header carries the boundary, so
    /// it is put back in front of the body to make a complete entity to parse.
    ///
    /// Decoded as UTF-8 explicitly. Browsers send form fields in the page's encoding and name no
    /// charset on the part, and the pages declare UTF-8.
    /// </remarks>
    private static FormBody ReadMultipart(byte[] body, string contentType)
    {
        try
        {
            using var stream = new MemoryStream();
            stream.Write(Encoding.ASCII.GetBytes($"Content-Type: {contentType}\r\n\r\n"));
            stream.Write(body);
            stream.Position = 0;

            if (MimeEntity.Load(stream) is not Multipart multipart)
                return Nothing;

            var named = multipart
                .OfType<TextPart>()
                .Select(part => new
                {
                    Name = part.ContentDisposition is not null
                           && part.ContentDisposition.Parameters.TryGetValue("name", out string? partName)
                        ? partName
                        : null,
                    Value = part.GetText(Encoding.UTF8)
                })
                .Where(part => part.Name is not null);

            return new FormBody(named.ToLookup(part => part.Name!, part => part.Value));
        }
        catch (Exception exception) when (exception is FormatException or ParseException)
        {
            return Nothing;
        }
    }

    /// <summary>The fields out of a URL-encoded body, which is what a form with no enctype sends.</summary>
    private static FormBody ReadUrlEncoded(byte[] body)
    {
        var parsed = HttpUtility.ParseQueryString(Encoding.UTF8.GetString(body));

        var named = parsed.AllKeys
            .Where(key => key is not null)
            .SelectMany(key => (parsed.GetValues(key) ?? [])
                .Select(value => new { Name = key!, Value = value }));

        return new FormBody(named.ToLookup(entry => entry.Name, entry => entry.Value));
    }

    /// <summary>
    /// A request header by name, ignoring case, or the empty string.
    /// </summary>
    /// <remarks>
    /// Case-insensitive because API Gateway passes header names through as the client sent them,
    /// and HTTP/2 clients send them lower-cased.
    /// </remarks>
    private static string FindHeader(APIGatewayProxyRequest request, string name) =>
        request.Headers?
            .FirstOrDefault(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Value
        ?? string.Empty;
}
