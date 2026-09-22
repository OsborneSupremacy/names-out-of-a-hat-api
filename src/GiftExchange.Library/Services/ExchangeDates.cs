namespace GiftExchange.Library.Services;

/// <summary>
/// Every rule that measures something from the day an exchange happens.
/// </summary>
/// <remarks>
/// The date is a day on a calendar in somebody's home, and nothing records where that home is. So
/// "has it passed" is answered for the last place on Earth to reach it: a day is over once it is
/// over at UTC-12. That is up to a day late for most organizers, and nothing measured from here is
/// precise to the day anyway -- the prompt waits a week and the purge a year and a half. Being late
/// is harmless. Being early would prompt somebody to close an exchange whose party is tonight.
///
/// <see cref="DateOnly.MinValue"/> means the organizer gave no date, and nothing here treats it as
/// a date that passed long ago. An exchange without a date is never prompted and never purged.
/// </remarks>
internal static class ExchangeDates
{
    /// <summary>
    /// How long after the exchange the organizer is asked to close it, if they have not.
    /// </summary>
    /// <remarks>
    /// A week, so that a party that slipped by a few days has happened before anybody is asked
    /// about it, while the exchange is still fresh enough that closing it feels like the end of
    /// something rather than housekeeping.
    /// </remarks>
    internal const int ClosePromptDelayDays = 7;

    /// <summary>
    /// How long after the exchange it is deleted, closed or not.
    /// </summary>
    /// <remarks>
    /// Eighteen months, and the number is set by copying rather than by privacy alone. Most
    /// exchanges happen once a year, and next year's is made by copying this one, which needs this
    /// one's people, addresses and picks to still be here. Eighteen months leaves room for a year
    /// and a date that drifted; anything much shorter would quietly break the feature that makes
    /// the second year easy.
    /// </remarks>
    internal const int RetentionMonths = 18;

    /// <summary>How far ahead a date may be set.</summary>
    private const int FurthestAheadYears = 2;

    /// <summary>The offset of the last time zone to reach any given day.</summary>
    private static readonly TimeSpan LastTimeZone = TimeSpan.FromHours(-12);

    /// <summary>The latest date an organizer may give.</summary>
    internal static DateOnly Latest(DateTimeOffset now) =>
        TodayEverywhere(now).AddYears(FurthestAheadYears);

    /// <summary>Whether the day is over in every time zone. Never true of no date.</summary>
    internal static bool HasPassed(DateOnly date, DateTimeOffset now) =>
        date != DateOnly.MinValue && date < TodayEverywhere(now);

    /// <summary>
    /// Exchanges dated before this, and not undated, are due a close prompt.
    /// </summary>
    internal static DateOnly ClosePromptCutoff(DateTimeOffset now) =>
        TodayEverywhere(now).AddDays(-ClosePromptDelayDays);

    /// <summary>
    /// Exchanges dated before this, and not undated, are due to be deleted.
    /// </summary>
    internal static DateOnly PurgeCutoff(DateTimeOffset now) =>
        TodayEverywhere(now).AddMonths(-RetentionMonths);

    /// <summary>The date at UTC-12, which is the earliest date anybody is still living in.</summary>
    private static DateOnly TodayEverywhere(DateTimeOffset now) =>
        DateOnly.FromDateTime(now.ToOffset(LastTimeZone).DateTime);
}
