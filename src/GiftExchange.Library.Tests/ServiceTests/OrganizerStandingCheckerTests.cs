using GiftExchange.Library.Contexts;
using GiftExchange.Library.Entities;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The thresholds, against a real database, because half of what is being pinned down is the
/// counting: distinct people, a union across two tables, a window, and one organizer's reports
/// kept apart from another's.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OrganizerStandingCheckerTests
{
    static OrganizerStandingCheckerTests() => DotEnv.Load();

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly OrganizerStandingChecker _sut;

    public OrganizerStandingCheckerTests(PostgresFixture dbFixture)
    {
        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _sut = serviceProvider.GetRequiredService<OrganizerStandingChecker>();
    }

    [Fact]
    public async Task AnOrganizerNobodyHasReported_MaySend()
    {
        // act
        var standing = await _sut.CheckAsync(Organizer());

        // assert
        standing.MaySend.Should().BeTrue();
        standing.RefusalMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task OneComplaintShortOfTheThreshold_MayStillSend()
    {
        // arrange
        var organizer = Organizer();
        await ComplainAsync(organizer, OrganizerStandingChecker.SuspendAtComplaints - 1);

        // act
        var standing = await _sut.CheckAsync(organizer);

        // assert
        standing.MaySend.Should().BeTrue();
    }

    [Fact]
    public async Task EnoughComplaints_StopTheOrganizerSending()
    {
        // arrange
        var organizer = Organizer();
        await ComplainAsync(organizer, OrganizerStandingChecker.SuspendAtComplaints);

        // act: asked in a different case from the one stored, as an organizer's session may.
        var standing = await _sut.CheckAsync(organizer.ToUpperInvariant());

        // assert
        standing.MaySend.Should().BeFalse();
        standing.RefusalStatusCode.Should().Be(HttpStatusCode.Forbidden);
        standing.RefusalMessage.Should().Be(OrganizerStandingChecker.RefusalMessage);
    }

    [Fact]
    public async Task ComplaintsOlderThanTheWindow_NoLongerCount()
    {
        // arrange
        var organizer = Organizer();
        var outside = DateTimeOffset.UtcNow.Subtract(OrganizerStandingChecker.Window).AddDays(-1);

        await ComplainAsync(organizer, OrganizerStandingChecker.SuspendAtComplaints, outside);

        // act
        var standing = await _sut.CheckAsync(organizer);

        // assert
        standing.MaySend.Should().BeTrue("the suspension lifts on its own once the reports age out");
    }

    [Fact]
    public async Task ComplaintsAgainstSomebodyElse_DoNotCount()
    {
        // arrange
        await ComplainAsync(Organizer(), OrganizerStandingChecker.SuspendAtComplaints);

        // act
        var standing = await _sut.CheckAsync(Organizer());

        // assert
        standing.MaySend.Should().BeTrue();
    }

    /// <summary>
    /// Fewer complaints than stop an organizer alone, and enough people between the two lists to
    /// stop them together.
    /// </summary>
    [Fact]
    public async Task ComplaintsAndRefusalsTogether_StopTheOrganizerSending()
    {
        // arrange
        var organizer = Organizer();
        const int complaints = OrganizerStandingChecker.SuspendAtComplaints - 1;

        await ComplainAsync(organizer, complaints);
        await RefuseAsync(organizer, Addresses(OrganizerStandingChecker.SuspendAtRefusals - complaints));

        // act
        var standing = await _sut.CheckAsync(organizer);

        // assert
        standing.MaySend.Should().BeFalse();
    }

    /// <summary>
    /// Somebody who complained and also ticked the box on the leave page is one person, and an
    /// organizer is not suspended on the strength of fewer people than the threshold says.
    /// </summary>
    [Fact]
    public async Task SomebodyWhoComplainedAndRefused_IsCountedOnce()
    {
        // arrange
        var organizer = Organizer();
        var complainants = await ComplainAsync(organizer, OrganizerStandingChecker.SuspendAtComplaints - 1);

        // Every complainant refuses as well, plus people up to one short of the threshold.
        var others = OrganizerStandingChecker.SuspendAtRefusals - 1 - complainants.Count;
        await RefuseAsync(organizer, [.. complainants, .. Addresses(others)]);

        // act
        var standing = await _sut.CheckAsync(organizer);

        // assert
        standing.MaySend.Should().BeTrue();
    }

    /// <summary>
    /// The case bounces are counted for: most of a list the organizer did not collect does not
    /// exist.
    /// </summary>
    [Fact]
    public async Task EnoughOfWhatWasSentBouncing_StopsTheOrganizerSending()
    {
        // arrange
        var organizer = Organizer();
        var mailed = Addresses(40);

        await SentAsync(organizer, mailed);
        await BounceAsync(organizer, [.. mailed.Take(OrganizerStandingChecker.SuspendAtBounces)]);

        // act
        var standing = await _sut.CheckAsync(organizer);

        // assert: five of forty is twelve and a half percent.
        standing.MaySend.Should().BeFalse();
        standing.RefusalStatusCode.Should().Be(HttpStatusCode.Forbidden);
        standing.RefusalMessage.Should().Be(OrganizerStandingChecker.BounceRefusalMessage);
    }

    /// <summary>
    /// A large exchange collects typos by being large. Judged as a share, the same number that
    /// stops somebody mailing forty does not stop somebody mailing two hundred.
    /// </summary>
    [Fact]
    public async Task TheSameBouncesAcrossMoreMail_DoNotStopTheOrganizer()
    {
        // arrange
        var organizer = Organizer();
        var mailed = Addresses(200);

        await SentAsync(organizer, mailed);
        await BounceAsync(organizer, [.. mailed.Take(OrganizerStandingChecker.SuspendAtBounces)]);

        // act
        var standing = await _sut.CheckAsync(organizer);

        // assert
        standing.MaySend.Should().BeTrue();
    }

    /// <summary>
    /// One typo in a family of eight is twelve percent, and says nothing about the organizer.
    /// </summary>
    [Fact]
    public async Task AHighShareOfVeryFewBounces_DoesNotStopTheOrganizer()
    {
        // arrange
        var organizer = Organizer();
        var mailed = Addresses(8);

        await SentAsync(organizer, mailed);
        await BounceAsync(organizer, [.. mailed.Take(OrganizerStandingChecker.SuspendAtBounces - 1)]);

        // act
        var standing = await _sut.CheckAsync(organizer);

        // assert
        standing.MaySend.Should().BeTrue();
    }

    /// <summary>
    /// Mail sent before the ledger existed has bounces and no sends to divide them by. That is
    /// evidence of bounces and not of a rate above one, and it must not throw.
    /// </summary>
    [Fact]
    public async Task BouncesWithNoSendsBehindThem_AreJudgedAsIfEveryoneMailedBounced()
    {
        // arrange
        var organizer = Organizer();
        await BounceAsync(organizer, Addresses(OrganizerStandingChecker.SuspendAtBounces));

        // act
        var standing = await _sut.CheckAsync(organizer);

        // assert
        standing.MaySend.Should().BeFalse();
    }

    [Fact]
    public async Task BouncesOlderThanTheWindow_NoLongerCount()
    {
        // arrange
        var organizer = Organizer();
        var outside = DateTimeOffset.UtcNow.Subtract(OrganizerStandingChecker.Window).AddDays(-1);

        await BounceAsync(organizer, Addresses(OrganizerStandingChecker.SuspendAtBounces), outside);

        // act
        var standing = await _sut.CheckAsync(organizer);

        // assert
        standing.MaySend.Should().BeTrue();
    }

    /// <summary>
    /// An organizer both reported and bouncing is told about the reports, which are the stronger
    /// thing and the one they are more likely to be able to do something about.
    /// </summary>
    [Fact]
    public async Task ComplaintsAndBouncesTogether_AreReportedAsComplaints()
    {
        // arrange
        var organizer = Organizer();
        await ComplainAsync(organizer, OrganizerStandingChecker.SuspendAtComplaints);
        await BounceAsync(organizer, Addresses(OrganizerStandingChecker.SuspendAtBounces));

        // act
        var standing = await _sut.CheckAsync(organizer);

        // assert
        standing.RefusalMessage.Should().Be(OrganizerStandingChecker.RefusalMessage);
    }

    private static string Organizer() => $"organizer-{Guid.NewGuid():N}@example.com";

    private static ImmutableList<string> Addresses(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => $"{Guid.NewGuid():N}@example.com")];

    private async Task<ImmutableList<string>> ComplainAsync(
        string organizer,
        int count,
        DateTimeOffset? complainedAt = null
    )
    {
        var complainants = Addresses(count);

        await using var context = await _contextFactory.CreateDbContextAsync();

        context.OrganizerComplaints.AddRange(complainants.Select(email => new OrganizerComplaintEntity
        {
            OrganizerComplaintId = Guid.CreateVersion7(),
            OrganizerEmailNormalized = organizer,
            EmailNormalized = email,
            ComplainedAt = complainedAt ?? DateTimeOffset.UtcNow.AddDays(-1)
        }));

        await context.SaveChangesAsync();

        return complainants;
    }

    private async Task RefuseAsync(string organizer, ImmutableList<string> emails)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.DoNotAddByOrganizer.AddRange(emails.Select(email => new DoNotAddByOrganizerEntity
        {
            DoNotAddByOrganizerId = Guid.CreateVersion7(),
            OrganizerEmailNormalized = organizer,
            EmailNormalized = email,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        }));

        await context.SaveChangesAsync();
    }

    private async Task SentAsync(string organizer, ImmutableList<string> emails)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.OrganizerSends.AddRange(emails.Select(email => new OrganizerSendEntity
        {
            OrganizerSendId = Guid.CreateVersion7(),
            ParticipantId = Guid.NewGuid(),
            OrganizerEmailNormalized = organizer,
            EmailNormalized = email,
            SentAt = DateTimeOffset.UtcNow.AddDays(-2)
        }));

        await context.SaveChangesAsync();
    }

    private async Task BounceAsync(string organizer, ImmutableList<string> emails, DateTimeOffset? bouncedAt = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.OrganizerBounces.AddRange(emails.Select(email => new OrganizerBounceEntity
        {
            OrganizerBounceId = Guid.CreateVersion7(),
            OrganizerEmailNormalized = organizer,
            EmailNormalized = email,
            BouncedAt = bouncedAt ?? DateTimeOffset.UtcNow.AddDays(-1)
        }));

        await context.SaveChangesAsync();
    }
}
