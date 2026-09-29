namespace GiftExchange.Library.Extensions;

internal static class StringExtensions
{
    extension(string input)
    {
        public string TrimNullSafe()
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return input.Trim();
        }

        public bool ContentEquals(string value) =>
            input.TrimNullSafe().Equals(value.TrimNullSafe(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The form an email address is stored and compared in on the do-not-add lists: trimmed and
        /// lower-cased.
        /// </summary>
        /// <remarks>
        /// Normalised on the way in rather than at the point of comparison, so that a check is an
        /// index seek on the column rather than a scan calling <c>lower()</c> on every row. Every
        /// other address column in this schema is compared with <see cref="ContentEquals"/>, which
        /// is fine for a handful of participants held in memory and would not be fine for a list
        /// that grows with everybody who has ever refused an invitation.
        ///
        /// Invariant lower-casing, not the current culture's. The Turkish dotless i turns an
        /// ASCII 'I' into something that no longer matches the address it came from, and an address
        /// that stops matching itself is a block that silently stops blocking.
        /// </remarks>
        public string ToNormalizedEmail() =>
            input.TrimNullSafe().ToLowerInvariant();

        /// <summary>
        /// The inbox an address delivers to, for the questions that are about a person rather than
        /// a spelling: how much one organizer has sent, what has been held against them, and how
        /// often one inbox can be sent a sign-in link.
        /// </summary>
        /// <remarks>
        /// <see cref="ToNormalizedEmail"/>, then without a <c>+tag</c>, and for Gmail without the
        /// dots Gmail ignores. Without this, <c>me+1@gmail.com</c> and <c>me+2@gmail.com</c> were two
        /// organizers with two sets of limits and two clean records, and one inbox could hold as
        /// many of them as it cared to invent.
        ///
        /// A key, never an address. Nothing is sent to it, and the address somebody signed in with
        /// stays who they are: their exchanges, their session and their mail all keep the spelling
        /// they typed. Two genuinely different people who differ only by a <c>+tag</c> at a provider
        /// that treats <c>+</c> as an ordinary character would share limits, which is rare enough,
        /// and harmless enough, to accept.
        /// </remarks>
        public string ToMailboxKey()
        {
            var normalized = input.ToNormalizedEmail();

            var at = normalized.LastIndexOf('@');

            if (at <= 0)
                return normalized;

            var local = normalized[..at];
            var domain = normalized[(at + 1)..];

            var plus = local.IndexOf('+');

            if (plus > 0)
                local = local[..plus];

            if (domain is "gmail.com" or "googlemail.com")
            {
                local = local.Replace(".", string.Empty);
                domain = "gmail.com";
            }

            return string.IsNullOrEmpty(local)
                ? normalized
                : $"{local}@{domain}";
        }

        public static Guid ToGuidOrEmpty(string value) =>
            Guid.TryParse(value, out var guid) ? guid : Guid.Empty;
    }
}
