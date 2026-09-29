using System.Security.Cryptography;

namespace GiftExchange.Library.Utility;

/// <summary>
/// Whether a request came through the CloudFront distribution in front of the API, rather than
/// straight to the API Gateway endpoint behind it.
/// </summary>
/// <remarks>
/// Everything that protects the API from strangers sits on that distribution: the web ACL and its
/// per-address rate limits, and the geographic restriction. The execute-api endpoint behind it is
/// public too, answers every route, and has none of those, so a script that finds it walks around
/// all of them. CloudFront adds a secret header on its way to the origin, and a request without it
/// did not come through CloudFront.
///
/// The secret proves where a request came from and nothing else. It authenticates nobody, and it is
/// not something a browser ever holds.
///
/// Two settings, so it can be turned on without an outage. CloudFront takes minutes to start sending
/// a new header and a Lambda configuration change takes seconds, so enforcing in the same apply that
/// introduces the header would refuse real traffic in between. With the secret set and enforcement
/// off, a missing or wrong header is logged and let through; once the logs have gone quiet,
/// enforcement is switched on. With no secret set at all, there is nothing to check.
/// </remarks>
internal class OriginGuard
{
    internal const string HeaderName = "X-Origin-Verify";

    private readonly byte[] _secret;

    private readonly bool _enforced;

    public OriginGuard(string secret, bool enforced)
    {
        _secret = Encoding.UTF8.GetBytes(secret ?? string.Empty);
        _enforced = enforced;
    }

    public static OriginGuard FromEnvironment() =>
        new(
            EnvReader.TryGetStringValue("ORIGIN_VERIFY_SECRET", out var secret) ? secret : string.Empty,
            EnvReader.TryGetBooleanValue("ORIGIN_VERIFY_ENFORCED", out var enforced) && enforced
        );

    /// <summary>
    /// What to do with a request, given the headers it arrived with.
    /// </summary>
    public OriginVerdict Check(IDictionary<string, string>? headers)
    {
        if (_secret.Length == 0)
            return OriginVerdict.Allow;

        if (CarriesSecret(headers))
            return OriginVerdict.Allow;

        return _enforced ? OriginVerdict.Refuse : OriginVerdict.AllowButReport;
    }

    private bool CarriesSecret(IDictionary<string, string>? headers)
    {
        // API Gateway hands headers over with whatever casing they arrived in.
        var presented = headers?
            .FirstOrDefault(header => header.Key.Equals(HeaderName, StringComparison.OrdinalIgnoreCase))
            .Value;

        if (string.IsNullOrEmpty(presented))
            return false;

        // Fixed-time, so how long a refusal takes says nothing about how much of a guess was right.
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), _secret);
    }
}

internal enum OriginVerdict
{
    Allow,
    AllowButReport,
    Refuse
}
