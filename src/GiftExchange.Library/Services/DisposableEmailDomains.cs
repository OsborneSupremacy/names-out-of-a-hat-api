using System.Collections.Frozen;

namespace GiftExchange.Library.Services;

/// <summary>
/// Recognises addresses at throwaway mail services, so that nobody can organize a gift exchange
/// from an inbox they will abandon the moment it has been used to reach other people.
/// </summary>
/// <remarks>
/// The list is disposable-email-domains/disposable-email-domains on GitHub (CC0), copied into
/// Resources rather than pulled in through a package. A scheduled workflow opens a pull request
/// when upstream changes, so an update is something to read before it ships rather than something
/// that arrives with a dependency bump.
///
/// Upstream leaves out forwarding services such as Apple's Hide My Email, Firefox Relay and
/// SimpleLogin. That is deliberate and agrees with us: those hide a real, lasting inbox behind an
/// alias, which is somebody protecting themselves rather than somebody preparing to disappear.
///
/// Only organizers are checked. A participant with a throwaway address is harming nobody, and the
/// people this protects are the ones an organizer emails, not the organizer.
/// </remarks>
internal static class DisposableEmailDomains
{
    internal const string RejectionMessage =
        "Please use a permanent email address. Addresses from temporary email services can't be used to organize a gift exchange.";

    private const string ResourceName = "GiftExchange.Library.Resources.disposable_email_blocklist.conf";

    private static readonly Lazy<FrozenSet<string>> Domains = new(Load);

    /// <summary>
    /// True when the address's domain, or any domain it is a subdomain of, is on the list. Some of
    /// these services hand out a fresh subdomain per inbox, so an exact match alone would miss them.
    /// </summary>
    internal static bool IsDisposable(string email)
    {
        var at = email.LastIndexOf('@');
        if (at < 0)
            return false;

        var domain = email[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();

        while (domain.Length > 0)
        {
            if (Domains.Value.Contains(domain))
                return true;

            var dot = domain.IndexOf('.');
            domain = dot < 0 ? string.Empty : domain[(dot + 1)..];
        }

        return false;
    }

    private static FrozenSet<string> Load()
    {
        using var stream = typeof(DisposableEmailDomains).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);

        return reader
            .ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.ToLowerInvariant())
            .ToFrozenSet(StringComparer.Ordinal);
    }
}
