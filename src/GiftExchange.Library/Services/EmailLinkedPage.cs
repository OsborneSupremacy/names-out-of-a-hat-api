using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// The shell every page this API serves to somebody arriving from an email is wrapped in.
/// </summary>
/// <remarks>
/// Hand-written HTML with inline styles and no scripts, for the reason <see cref="AskPageComposer"/>
/// gives at length: there is no front end to serve these from, and adding one for a handful of short
/// pages would be a poor trade. No script also means a form has to work as a form.
///
/// Extracted here when the leave pages arrived and became the second caller. Two copies of a page
/// shell is exactly the shape that drifts — one gets a viewport tag or a padding change and the
/// other does not — and somebody following a link out of an email is about to be asked to trust the
/// page with an action, so a page that does not look like the product it claims to be is a poor
/// thing to ask that of.
///
/// The one thing these pages ask the network for is the wordmark, which is worth the exception. It
/// is a single image served from the site these pages belong to, nothing depends on it arriving —
/// <see cref="Branding.LogoAltText"/> stands in when it does not — and a scanner fetching the page
/// ahead of the reader may fetch it too, which costs nothing, since the GET has no side effects.
/// </remarks>
internal static class EmailLinkedPage
{
    /// <summary>
    /// What every page in this shell may load, which is almost nothing: its inline styles, the
    /// wordmark from the site, and a form that posts back to where it came from. No script at all,
    /// so markup that ever slipped past encoding would still have nothing to run.
    /// </summary>
    internal const string ContentSecurityPolicy =
        "default-src 'none'; " +
        $"img-src {Branding.SiteUrl}; " +
        "style-src 'unsafe-inline'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'none'";

    /// <summary>
    /// The headers every page in this shell is served with, plus the page's own.
    /// </summary>
    /// <remarks>
    /// In one place so a page cannot be added without them. The referrer is withheld for all of
    /// them, not only the invitation, because every one of these pages carries a token in its
    /// address. A page's own header wins over one of these if both name it.
    /// </remarks>
    internal static Dictionary<string, string> Headers(Dictionary<string, string> ownHeaders)
    {
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/html; charset=utf-8",
            ["Content-Security-Policy"] = ContentSecurityPolicy,
            ["X-Content-Type-Options"] = "nosniff",
            ["Referrer-Policy"] = "no-referrer",
            ["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains"
        };

        foreach (var (name, value) in ownHeaders)
            headers[name] = value;

        return headers;
    }

    /// <param name="heading">This application's own words. Encoded here all the same.</param>
    /// <param name="body">
    /// Markup, so nothing anybody typed may be passed in unencoded. Every composer building this
    /// runs organizer- and participant-supplied text through <c>HttpUtility.HtmlEncode</c> first.
    /// </param>
    internal static string Compose(string heading, string body) =>
        $"""
         <!doctype html>
         <html lang="en">
         <head>
           <meta charset="utf-8" />
           <meta name="viewport" content="width=device-width, initial-scale=1" />
           <title>{HttpUtility.HtmlEncode(heading)} &mdash; Names Out Of A Hat</title>
         </head>
         <body style="margin:0;padding:24px;background-color:#faf8f5;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;color:#222222;line-height:1.5;">
           <div style="max-width:560px;margin:0 auto;background-color:#ffffff;border-radius:8px;padding:32px;">
             <a href="{Branding.SiteUrl}" style="display:inline-block;margin:0 0 24px;"><img src="{Branding.LogoUrl}" alt="{Branding.LogoAltText}" width="260" height="87" style="display:block;border:0;max-width:100%;height:auto;" /></a>
             <h1 style="margin:0 0 16px;font-size:24px;">{HttpUtility.HtmlEncode(heading)}</h1>
             {body}
           </div>
         </body>
         </html>
         """;
}
