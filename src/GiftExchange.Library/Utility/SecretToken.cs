using System.Security.Cryptography;

namespace GiftExchange.Library.Utility;

/// <summary>
/// Unguessable tokens, and the one-way transform they are stored under.
///
/// A token is handed out once and never held anywhere afterwards — only <see cref="Hash"/> of it
/// is, so possession of the database is not possession of the token. Matching one that comes back
/// means hashing what arrived and looking for that instead.
/// </summary>
internal static class SecretToken
{
    /// <summary>
    /// 128 bits, for the gift ideas tokens.
    ///
    /// Enough that guessing is not a strategy. Sized when these tokens sat in an email address
    /// somebody might have to retype; they travel only in links now, and are kept this length so
    /// that links already issued and links issued today look the same.
    /// </summary>
    public const int LegibleTokenBytes = 16;

    /// <summary>
    /// 256 bits, for a token nobody ever has to look at.
    ///
    /// The length magic link tokens have always used. They appear only in a URL, so nothing is
    /// paid for the extra characters.
    /// </summary>
    public const int OpaqueTokenBytes = 32;

    /// <summary>
    /// A new token in the clear. This is the only time it exists outside whatever it is sent in;
    /// store <see cref="Hash"/> of it rather than the return value.
    /// </summary>
    /// <remarks>
    /// Base64url rather than plain base64: it yields only letters, digits, '-' and '_', all of
    /// which a URL path or query string may carry unencoded, where base64's '+' and '/' may not.
    /// </remarks>
    /// <param name="tokenBytes">
    /// How much randomness to draw. Stated by the caller rather than fixed here, because the two
    /// callers are bound by different things — see the constants above.
    /// </param>
    public static string Create(int tokenBytes = LegibleTokenBytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(tokenBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>Hex-encoded SHA-256, which is what a token column holds.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
