namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The rules measured from an exchange's date. All of them answer "has this day passed" for the
/// last time zone to reach it, so the interesting cases sit either side of midnight at UTC-12.
/// </summary>
public class ExchangeDatesTests
{
    private static readonly DateOnly Christmas = new(2026, 12, 25);

    [Fact]
    public void ADay_HasNotPassed_WhileItIsStillThatDayAtUtcMinus12()
    {
        // 11:59 on the 26th in UTC is 23:59 on the 25th at UTC-12.
        var now = new DateTimeOffset(2026, 12, 26, 11, 59, 0, TimeSpan.Zero);

        ExchangeDates.HasPassed(Christmas, now).Should().BeFalse();
    }

    [Fact]
    public void ADay_HasPassed_OnceItIsOverAtUtcMinus12()
    {
        var now = new DateTimeOffset(2026, 12, 26, 12, 0, 0, TimeSpan.Zero);

        ExchangeDates.HasPassed(Christmas, now).Should().BeTrue();
    }

    [Fact]
    public void NoDate_HasNeverPassed()
    {
        ExchangeDates.HasPassed(DateOnly.MinValue, DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void TheClosePrompt_IsDueAWeekAfterTheDayIsOverEverywhere()
    {
        // Christmas is over everywhere at 12:00 UTC on the 26th, so a week after that is 12:00 UTC
        // on 2 January.
        var aWeekLater = new DateTimeOffset(2027, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var justBefore = aWeekLater.AddMinutes(-1);

        (Christmas < ExchangeDates.ClosePromptCutoff(aWeekLater)).Should().BeTrue();
        (Christmas < ExchangeDates.ClosePromptCutoff(justBefore)).Should().BeFalse();
    }

    [Fact]
    public void ThePurge_IsDueEighteenMonthsAfterTheDate()
    {
        var eighteenMonthsLater = new DateTimeOffset(2028, 6, 26, 12, 0, 0, TimeSpan.Zero);
        var aDayEarlier = eighteenMonthsLater.AddDays(-1);

        (Christmas < ExchangeDates.PurgeCutoff(eighteenMonthsLater)).Should().BeTrue();
        (Christmas < ExchangeDates.PurgeCutoff(aDayEarlier)).Should().BeFalse();
    }

    [Fact]
    public void TheLatestDate_IsTwoYearsOut()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

        ExchangeDates.Latest(now).Should().Be(new DateOnly(2028, 9, 21));
    }

    [Fact]
    public void TheDate_IsPhrasedAsApproximate_WithTheWeekday()
    {
        ExchangeDatePhrasing.Describe(Christmas)
            .Should().Be("The gift exchange is planned for around Friday, December 25, 2026.");
    }
}
