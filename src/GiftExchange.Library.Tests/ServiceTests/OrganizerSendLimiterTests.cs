using GiftExchange.Library.Contexts;
using GiftExchange.Library.Entities;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The limits, against a real database, because what is being pinned down is mostly counting:
/// distinct addresses, two windows, an address staying inside a window until its last send leaves
/// it, and people already mailed costing nothing to mail again.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OrganizerSendLimiterTests
{
    static OrganizerSendLimiterTests() => DotEnv.Load();

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly OrganizerSendLimiter _sut;

    public OrganizerSendLimiterTests(PostgresFixture dbFixture)
    {
        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _sut = serviceProvider.GetRequiredService<OrganizerSendLimiter>();
    }

    [Fact]
    public async Task AnOrganizerWhoHasMailedNobody_MaySendAWholeExchange()
    {
        // act
        var result = await _sut.CheckAsync(Request(Organizer(), Addresses(ParticipantLimit.MaxParticipants)));

        // assert
        result.WithinLimit.Should().BeTrue();
        result.RefusalMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task ASendThatWouldReachExactlyTheDailyLimit_IsAllowed()
    {
        // arrange
        var organizer = Organizer();
        await SentAsync(organizer, Addresses(OrganizerSendLimiter.DailyLimit - 10), HoursAgo(2));

        // act
        var result = await _sut.CheckAsync(Request(organizer, Addresses(10)));

        // assert
        result.WithinLimit.Should().BeTrue();
    }

    [Fact]
    public async Task ASendThatWouldPassTheDailyLimit_IsRefusedWithATimeToComeBack()
    {
        // arrange
        var organizer = Organizer();
        var oldest = HoursAgo(20);

        await SentAsync(organizer, Addresses(40), oldest);
        await SentAsync(organizer, Addresses(OrganizerSendLimiter.DailyLimit - 40), HoursAgo(2));

        // act: asked in a different case from the one stored, as an organizer's session may.
        var result = await _sut.CheckAsync(Request(organizer.ToUpperInvariant(), Addresses(10)));

        // assert: ten more need room, and the forty oldest leave together.
        result.WithinLimit.Should().BeFalse();
        result.RefusalStatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        result.RefusalMessage.Should().Be(
            OrganizerSendLimiter.RefusalMessage(OrganizerSendLimiter.DailyLimit, "day", oldest.Add(OrganizerSendLimiter.DailyWindow)));
    }

    /// <summary>
    /// The case the limit exists for, and the one it must not punish: sending the same exchange
    /// again, or last year's copy of it, reaches nobody new.
    /// </summary>
    [Fact]
    public async Task MailingPeopleAlreadyMailed_CostsNothing()
    {
        // arrange
        var organizer = Organizer();
        var family = Addresses(OrganizerSendLimiter.DailyLimit);
        await SentAsync(organizer, family, HoursAgo(3));

        // act
        var result = await _sut.CheckAsync(Request(organizer, [.. family.Take(ParticipantLimit.MaxParticipants)]));

        // assert
        result.WithinLimit.Should().BeTrue();
    }

    [Fact]
    public async Task SendsOlderThanTheDay_DoNotCountAgainstIt()
    {
        // arrange
        var organizer = Organizer();
        await SentAsync(organizer, Addresses(OrganizerSendLimiter.DailyLimit), HoursAgo(25));

        // act
        var result = await _sut.CheckAsync(Request(organizer, Addresses(ParticipantLimit.MaxParticipants)));

        // assert
        result.WithinLimit.Should().BeTrue();
    }

    /// <summary>
    /// A day at a time under the daily limit still runs into the week, and the week is the time
    /// the organizer is given, because the day's would only be refused again.
    /// </summary>
    [Fact]
    public async Task ASendThatWouldPassTheWeeklyLimit_IsRefusedUntilTheWeekHasRoom()
    {
        // arrange
        var organizer = Organizer();
        var oldest = DaysAgo(6);

        await SentAsync(organizer, Addresses(90), oldest);
        await SentAsync(organizer, Addresses(90), DaysAgo(4));
        await SentAsync(organizer, Addresses(OrganizerSendLimiter.WeeklyLimit - 180), DaysAgo(2));

        // act
        var result = await _sut.CheckAsync(Request(organizer, Addresses(10)));

        // assert
        result.WithinLimit.Should().BeFalse();
        result.RefusalStatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        result.RefusalMessage.Should().Be(
            OrganizerSendLimiter.RefusalMessage(OrganizerSendLimiter.WeeklyLimit, "week", oldest.Add(OrganizerSendLimiter.WeeklyWindow)));
    }

    /// <summary>
    /// Somebody mailed early and again later is still inside the window until the later send
    /// leaves it, so the time given is when room really opens rather than when it first seems to.
    /// </summary>
    [Fact]
    public async Task SomebodyMailedTwice_LeavesTheWindowWithTheirLastSend()
    {
        // arrange
        var organizer = Organizer();
        var early = Addresses(OrganizerSendLimiter.DailyLimit);
        var later = HoursAgo(5);

        await SentAsync(organizer, early, HoursAgo(20));
        await SentAsync(organizer, early, later);

        // act
        var result = await _sut.CheckAsync(Request(organizer, Addresses(1)));

        // assert
        result.RefusalMessage.Should().Be(
            OrganizerSendLimiter.RefusalMessage(OrganizerSendLimiter.DailyLimit, "day", later.Add(OrganizerSendLimiter.DailyWindow)));
    }

    [Fact]
    public async Task TheOrganizersOwnAddress_DoesNotCount()
    {
        // arrange
        var organizer = Organizer();
        await SentAsync(organizer, Addresses(OrganizerSendLimiter.DailyLimit - 1), HoursAgo(1));

        // act: one new person, and the organizer's own copy of the invitation.
        var result = await _sut.CheckAsync(Request(organizer, [.. Addresses(1), organizer]));

        // assert
        result.WithinLimit.Should().BeTrue();
    }

    [Fact]
    public async Task SendsBySomebodyElse_DoNotCount()
    {
        // arrange
        await SentAsync(Organizer(), Addresses(OrganizerSendLimiter.WeeklyLimit), HoursAgo(1));

        // act
        var result = await _sut.CheckAsync(Request(Organizer(), Addresses(ParticipantLimit.MaxParticipants)));

        // assert
        result.WithinLimit.Should().BeTrue();
    }

    private static CheckOrganizerSendLimitRequest Request(string organizer, ImmutableList<string> recipients) =>
        new() { OrganizerEmail = organizer, RecipientEmails = recipients };

    private static string Organizer() => $"organizer-{Guid.NewGuid():N}@example.com";

    private static ImmutableList<string> Addresses(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => $"{Guid.NewGuid():N}@example.com")];

    /// <summary>Whole seconds, so what the database hands back compares equal to what was written.</summary>
    private static DateTimeOffset HoursAgo(int hours) => Truncated(DateTimeOffset.UtcNow.AddHours(-hours));

    private static DateTimeOffset DaysAgo(int days) => Truncated(DateTimeOffset.UtcNow.AddDays(-days));

    private static DateTimeOffset Truncated(DateTimeOffset moment) =>
        new(moment.Ticks - moment.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);

    private async Task SentAsync(string organizer, ImmutableList<string> recipients, DateTimeOffset sentAt)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.OrganizerSends.AddRange(recipients.Select(email => new OrganizerSendEntity
        {
            OrganizerSendId = Guid.CreateVersion7(),
            ParticipantId = Guid.NewGuid(),
            OrganizerEmailNormalized = organizer,
            EmailNormalized = email,
            SentAt = sentAt
        }));

        await context.SaveChangesAsync();
    }
}
