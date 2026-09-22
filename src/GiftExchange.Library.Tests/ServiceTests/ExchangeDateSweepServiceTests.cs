using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using GiftExchange.Library.Contexts;
using Microsoft.Extensions.Logging;
using MimeKit;
using NSubstitute;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The daily sweep: close prompts a week after an exchange's date, deletion eighteen months after.
///
/// The sweep reads every organizer's hats, and the suite shares one database, so nothing here
/// counts totals. Every assertion is about the exchanges the test itself made, found by id or by
/// the organizer's address. The hat faker leaves the date unset for the same reason, which keeps
/// the rest of the suite's hats out of the sweep's way.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ExchangeDateSweepServiceTests
{
    static ExchangeDateSweepServiceTests()
    {
        // Static for the reason UndeliverableInvitationsServiceTests gives: AutomaticEmailSender
        // reads LIVE_MODE in its constructor, which runs in a field initialiser below.
        DotEnv.Load();
        Environment.SetEnvironmentVariable("LIVE_MODE", "true");
    }

    private readonly IAmazonSimpleEmailService _ses = Substitute.For<IAmazonSimpleEmailService>();

    private readonly List<byte[]> _sent = [];

    private readonly GiftExchangeProvider _provider;

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly ExchangeDateSweepService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    public ExchangeDateSweepServiceTests(PostgresFixture dbFixture)
    {
        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();

        _ses.When(ses => ses.SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var buffer = new MemoryStream();
                var data = ((SendRawEmailRequest)call[0]).RawMessage.Data;
                data.Position = 0;
                data.CopyTo(buffer);
                _sent.Add(buffer.ToArray());
            });

        _sut = new ExchangeDateSweepService(
            _provider,
            new ClosePromptEmailCompositionService(),
            new AutomaticEmailSender(_ses, Substitute.For<ILogger<AutomaticEmailSender>>()),
            Substitute.For<ILogger<ExchangeDateSweepService>>());
    }

    [Fact]
    public async Task AnOpenExchangeAWeekPastItsDate_PromptsTheOrganizerOnce()
    {
        // arrange
        var date = Today.AddDays(-10);
        var hat = await SeedAsync(date, HatStatus.CooledOff);

        // act
        await _sut.ExecuteAsync(Now);
        await _sut.ExecuteAsync(Now);

        // assert: once, not once per run.
        var prompt = SentTo(hat.OrganizerEmail).Should().ContainSingle().Subject;

        prompt.Subject.Should().Contain(hat.HatName);
        prompt.HtmlBody.Should().Contain(ExchangeDatePhrasing.Format(date));
        prompt.HtmlBody.Should().Contain($"/gift-exchange/{hat.HatId}");
        prompt.HtmlBody.Should().Contain("can't be undone");
    }

    [Fact]
    public async Task InvitationsSent_IsPromptedToo()
    {
        // arrange
        var hat = await SeedAsync(Today.AddDays(-10), HatStatus.InvitationsSent);

        // act
        await _sut.ExecuteAsync(Now);

        // assert
        SentTo(hat.OrganizerEmail).Should().ContainSingle();
    }

    [Theory]
    [InlineData(-3, "READY_TO_CLOSE")]
    [InlineData(-10, "IN_PROGRESS")]
    [InlineData(-10, "NAMES_ASSIGNED")]
    [InlineData(-10, "CLOSED")]
    public async Task ExchangesThatAreNotDue_AreNotPrompted(int daysAgo, string status)
    {
        // arrange
        var hat = await SeedAsync(Today.AddDays(daysAgo), status);

        // act
        await _sut.ExecuteAsync(Now);

        // assert
        SentTo(hat.OrganizerEmail).Should().BeEmpty();
    }

    [Fact]
    public async Task AnExchangeWithNoDate_IsNeverPromptedOrDeleted()
    {
        // arrange
        var hat = await SeedAsync(DateOnly.MinValue, HatStatus.CooledOff);

        // act
        await _sut.ExecuteAsync(Now);

        // assert
        SentTo(hat.OrganizerEmail).Should().BeEmpty();
        (await HatExistsAsync(hat.HatId)).Should().BeTrue();
    }

    [Fact]
    public async Task AnExchangeEighteenMonthsPastItsDate_IsDeleted_WithThePeopleOnlyInIt()
    {
        // arrange: one exchange past retention, and a second, undated one sharing a participant.
        var expired = await SeedAsync(Today.AddMonths(-19), HatStatus.Closed);
        var onlyInExpired = await AddParticipantAsync(expired);
        var inBoth = await AddParticipantAsync(expired);

        var other = await SeedAsync(DateOnly.MinValue, HatStatus.InProgress);
        await _provider.CreateParticipantAsync(
            new AddParticipantRequest
            {
                HatId = other.HatId,
                OrganizerEmail = other.OrganizerEmail,
                Name = inBoth.Person.Name,
                Email = inBoth.Person.Email
            },
            []);

        // act
        var result = await _sut.ExecuteAsync(Now);

        // assert
        result.HatsPurged.Should().BeGreaterThanOrEqualTo(1);
        (await HatExistsAsync(expired.HatId)).Should().BeFalse();
        (await HatExistsAsync(other.HatId)).Should().BeTrue();

        (await PersonExistsAsync(onlyInExpired.Person.Email)).Should().BeFalse();
        (await PersonExistsAsync(inBoth.Person.Email)).Should().BeTrue();

        // The organizer has signed in, and is kept even though this was their only exchange.
        (await PersonExistsAsync(expired.OrganizerEmail)).Should().BeTrue();
    }

    [Fact]
    public async Task AnExchangeInsideTheRetentionWindow_IsKept()
    {
        // arrange
        var hat = await SeedAsync(Today.AddMonths(-17), HatStatus.Closed);

        // act
        await _sut.ExecuteAsync(Now);

        // assert
        (await HatExistsAsync(hat.HatId)).Should().BeTrue();
    }

    private async Task<HatDataModel> SeedAsync(DateOnly exchangeDate, string status)
    {
        var hat = _hatFaker.Generate() with { ExchangeDate = exchangeDate };
        await _provider.CreateHatAsync(hat);
        await AddParticipantAsync(hat);
        await _provider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, status);
        return hat;
    }

    private Task<Participant> AddParticipantAsync(HatDataModel hat) =>
        _provider.CreateParticipantAsync(
            _participantFaker.Generate() with { HatId = hat.HatId, OrganizerEmail = hat.OrganizerEmail },
            []);

    private async Task<bool> HatExistsAsync(Guid hatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Hats.AnyAsync(hat => hat.HatId == hatId);
    }

    private async Task<bool> PersonExistsAsync(string email)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Persons.AnyAsync(person => person.Email == email);
    }

    private ImmutableList<MimeMessage> SentTo(string email) =>
    [
        .. _sent
            .Select(raw => MimeMessage.Load(new MemoryStream(raw)))
            .Where(message => message.To.Mailboxes.Any(mailbox => mailbox.Address == email))
    ];
}
