using System.Globalization;

namespace GiftExchange.Library.Services;

/// <summary>
/// How the exchange date is put into a sentence in an email.
/// </summary>
/// <remarks>
/// "Around", because the organizer was asked for an approximate date and the invitation should not
/// promise more than they did. A party that moves by a day should not make the invitation wrong.
///
/// Spelled out in English with the weekday, rather than in any numeric form, because 12/11 means
/// two different days depending on who is reading it, and the weekday is usually what people
/// actually plan around.
/// </remarks>
internal static class ExchangeDatePhrasing
{
    internal static string Describe(DateOnly exchangeDate) =>
        $"The gift exchange is planned for around {Format(exchangeDate)}.";

    internal static string Format(DateOnly exchangeDate) =>
        exchangeDate.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
}
