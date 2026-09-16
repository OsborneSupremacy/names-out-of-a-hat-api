namespace GiftExchange.Library.Services;

/// <summary>
/// Where this application lives, and where its wordmark is served from.
/// </summary>
/// <remarks>
/// Shared by the emails and by the pages the Ask renders. Those are two different media and each
/// builds its own markup — a mail client needs dimension attributes an Outlook layout engine will
/// honour, a browser page does not — so what is held here is only what must not differ.
///
/// That is the asset itself. The logo is served by the deployed front end rather than by anything
/// this project controls, so renaming or moving it breaks every caller at once, and one place to
/// change is the difference between a one-line fix and a hunt through two unrelated files.
/// </remarks>
internal static class Branding
{
    internal const string SiteUrl = "https://namesoutofahat.com";

    /// <summary>
    /// Where the pages an email links to are served from.
    /// </summary>
    /// <remarks>
    /// The API rather than the site, because these pages are rendered by the handler that acts on
    /// them. See <c>AskPageComposer</c> for why that is the arrangement: a link followed out of an
    /// email is a GET, mail scanners fetch those, and the only safe shape is a GET that renders a
    /// form and a POST behind a button on it. Putting the form on the front end would mean a public
    /// endpoint for it to call and a second implementation of the same click-gate.
    /// </remarks>
    internal const string ApiUrl = "https://api.namesoutofahat.com";

    /// <summary>
    /// The base of a leave link. The token follows as a path segment.
    /// </summary>
    /// <remarks>
    /// Here rather than beside the composer that writes it, because two unrelated files need the
    /// same string: the fine print of an invitation puts the address into an email, and the confirm
    /// page posts its form back to the same address. They are built by different classes in
    /// different media, and the pair drifting apart would produce a link that renders a form which
    /// submits nowhere.
    /// </remarks>
    internal const string LeaveUrl = $"{ApiUrl}/leave";

    /// <summary>
    /// The organizer's own page for one exchange, on the front end rather than on the API.
    /// </summary>
    /// <remarks>
    /// Unlike the leave and ask links, this one is not a page this API renders and not something a
    /// mail scanner following it can act on: it is behind sign-in, and a visitor without a session
    /// gets the sign-in page. So it can point at the application proper, where the organizer has
    /// the delivery column and the buttons that fix an address.
    ///
    /// The path mirrors the route in <c>src/app/src/App.tsx</c>. Nothing in this project can check
    /// that, which is the argument for it being written down once here rather than built inside
    /// whichever email happens to need it.
    /// </remarks>
    internal static string HatUrl(Guid hatId) => $"{SiteUrl}/gift-exchange/{hatId}";

    /// <summary>
    /// Served from the front end's public root, so the deployed site is what hosts it.
    /// </summary>
    internal const string LogoUrl = $"{SiteUrl}/logo-horizontal.png";

    /// <summary>
    /// What stands in for the wordmark when the image does not arrive.
    /// </summary>
    /// <remarks>
    /// This matters mainly in email: most mail clients block remote images until the reader asks
    /// for them, so for the majority of recipients this text <em>is</em> the masthead. The Ask
    /// pages get the same string because they are the same product and there is no reason for the
    /// two to read differently.
    /// </remarks>
    internal const string LogoAltText = "Names Out Of A Hat";
}
