using GiftExchange.Library.Contexts;
using GiftExchange.Library.Entities;
using GiftExchange.Library.Utility;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// Sending the invitations for a shaken exchange, against a real database.
///
/// The provider is the real one, for the reason ShareGiftIdeasServiceTests gives: what a send
/// leaves behind — tokens, a status, a timestamp — is rows, and a stub cannot show them written or
/// show them not written. The queue and the scheduler are substitutes, because what matters about
/// them is what they were handed.
///
/// The refusals matter as much as the send. A refused send has to leave nothing behind that looks
/// as though it went: no mail queued, no token issued, no status moved, no schedule created.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EnqueueInvitationsServiceTests
{
    static EnqueueInvitationsServiceTests() => DotEnv.Load();

    private const string SenderIp = "203.0.113.7";

    private readonly IEmailQueue _queue = Substitute.For<IEmailQueue>();

    private readonly ISchedulerService _scheduler = Substitute.For<ISchedulerService>();

    private readonly List<GiftExchangeEmailRequest> _queued = [];

    private readonly GiftExchangeProvider _provider;

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly EnqueueInvitationsService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public EnqueueInvitationsServiceTests(PostgresFixture dbFixture)
    {
        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();

        _queue.EnqueueAsync(Arg.Do<GiftExchangeEmailRequest>(email => _queued.Add(email)))
            .Returns(Task.CompletedTask);

        _sut = new EnqueueInvitationsService(
            _provider,
            serviceProvider.GetRequiredService<ApiGatewayAdapter>(),
            new HatPreconditionValidator(
                Substitute.For<ILogger<HatPreconditionValidator>>(),
                _provider,
                new FakeContentModerationService()),
            new EmailCompositionService(),
            _queue,
            _scheduler,
            serviceProvider.GetRequiredService<OrganizerStandingChecker>());
    }

    [Fact]
    public async Task AShakenExchange_SendsEveryParticipantTheirInvitation()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.NamesAssigned);

        // act
        var result = await _sut.ExecuteAsync(Request(exchange), SenderIp);

        // assert
        result.IsFaulted.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        _queued.Select(email => email.RecipientEmail)
            .Should().BeEquivalentTo(exchange.ParticipantIds.Keys);

        foreach (var email in _queued)
        {
            email.MessageType.Should().Be(EmailMessageType.Invitation);
            email.HatId.Should().Be(exchange.HatId);
            email.ParticipantId.Should().Be(
                exchange.ParticipantIds[email.RecipientEmail],
                "the delivery events are matched to a participant by this tag");
            email.HtmlBody.Should().Contain(
                exchange.PickedNames[email.RecipientEmail],
                "each invitation names that participant's own pick");
        }
    }

    [Fact]
    public async Task AShakenExchange_IsMarkedSentAndBothSchedulesAreCountedFromTheSameMoment()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.NamesAssigned);

        // act
        await _sut.ExecuteAsync(Request(exchange), SenderIp);

        // assert
        var hat = await HatAsync(exchange.HatId);
        hat.Status.Should().Be(HatStatus.InvitationsSent);
        hat.InvitationsSentFromIp.Should().Be(SenderIp);

        await _scheduler.Received(1).CreateCooledOffScheduleAsync(
            Arg.Is<SendInvitationsRequest>(request => request.HatId == exchange.HatId),
            hat.InvitationsQueuedAt);
        await _scheduler.Received(1).CreateUndeliverableInvitationsScheduleAsync(
            Arg.Is<SendInvitationsRequest>(request => request.HatId == exchange.HatId),
            hat.InvitationsQueuedAt);
    }

    [Fact]
    public async Task AShakenExchange_IssuesATokenForEveryParticipant()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.NamesAssigned);

        // act
        await _sut.ExecuteAsync(Request(exchange), SenderIp);

        // assert
        (await GiftIdeaTokenCountAsync(exchange)).Should().Be(exchange.ParticipantIds.Count);
    }

    [Fact]
    public async Task AnExchangeNotYetShaken_IsRefusedAndNothingIsSent()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InProgress);

        // act
        var result = await _sut.ExecuteAsync(Request(exchange), SenderIp);

        // assert
        result.IsFaulted.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await NothingShouldHaveHappenedAsync(exchange, HatStatus.InProgress);
    }

    [Fact]
    public async Task WhenTheOrganizerIsSuspended_TheSendIsForbiddenAndNothingIsLeftBehind()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.NamesAssigned);
        await ComplainAsync(exchange.OrganizerEmail, OrganizerStandingChecker.SuspendAtComplaints);

        // act
        var result = await _sut.ExecuteAsync(Request(exchange), SenderIp);

        // assert
        result.IsFaulted.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        result.Exception!.Message.Should().Contain(OrganizerStandingChecker.RefusalMessage);

        await NothingShouldHaveHappenedAsync(exchange, HatStatus.NamesAssigned);
    }

    /// <summary>
    /// Below the threshold the organizer is only logged for review. Stopping them there would make
    /// the warning and the refusal the same thing.
    /// </summary>
    [Fact]
    public async Task WhenTheOrganizerIsOnlyUnderReview_TheInvitationsStillGo()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.NamesAssigned);
        await ComplainAsync(exchange.OrganizerEmail, OrganizerStandingChecker.SuspendAtComplaints - 1);

        // act
        var result = await _sut.ExecuteAsync(Request(exchange), SenderIp);

        // assert
        result.IsFaulted.Should().BeFalse();
        _queued.Should().HaveCount(exchange.ParticipantIds.Count);
    }

    /// <summary>
    /// The preconditions run first, so somebody asking to send another organizer's exchange learns
    /// that it does not exist for them, and nothing about their own standing.
    /// </summary>
    [Fact]
    public async Task ASuspendedOrganizerSendingSomebodyElsesExchange_IsToldItIsNotTheirs()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.NamesAssigned);
        var stranger = $"{Guid.NewGuid():N}@example.com";
        await ComplainAsync(stranger, OrganizerStandingChecker.SuspendAtComplaints);

        // act
        var result = await _sut.ExecuteAsync(Request(exchange) with { OrganizerEmail = stranger }, SenderIp);

        // assert
        result.IsFaulted.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await NothingShouldHaveHappenedAsync(exchange, HatStatus.NamesAssigned);
    }

    private static SendInvitationsRequest Request(Exchange exchange) =>
        new() { HatId = exchange.HatId, OrganizerEmail = exchange.OrganizerEmail };

    private async Task NothingShouldHaveHappenedAsync(Exchange exchange, string expectedStatus)
    {
        _queued.Should().BeEmpty();

        (await GiftIdeaTokenCountAsync(exchange)).Should().Be(0, "no token should exist for mail that never went");

        var hat = await HatAsync(exchange.HatId);
        hat.Status.Should().Be(expectedStatus);
        hat.InvitationsQueuedAt.Should().Be(DateTimeOffset.MinValue);

        await _scheduler.DidNotReceiveWithAnyArgs().CreateCooledOffScheduleAsync(default!, default);
        await _scheduler.DidNotReceiveWithAnyArgs().CreateUndeliverableInvitationsScheduleAsync(default!, default);
    }

    private async Task<HatEntity> HatAsync(Guid hatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Hats.AsNoTracking().SingleAsync(hat => hat.HatId == hatId);
    }

    private async Task<int> GiftIdeaTokenCountAsync(Exchange exchange)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var participantIds = exchange.ParticipantIds.Values.ToList();

        return await context.GiftIdeaTokens.CountAsync(token => participantIds.Contains(token.ParticipantId));
    }

    private async Task ComplainAsync(string organizerEmail, int count)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.OrganizerComplaints.AddRange(Enumerable
            .Range(0, count)
            .Select(_ => new OrganizerComplaintEntity
            {
                OrganizerComplaintId = Guid.CreateVersion7(),
                OrganizerEmailNormalized = organizerEmail.Trim().ToLowerInvariant(),
                EmailNormalized = $"{Guid.NewGuid():N}@example.com",
                ComplainedAt = DateTimeOffset.UtcNow.AddDays(-1)
            }));

        await context.SaveChangesAsync();
    }

    /// <summary>Three participants in a ring, so every pick resolves to a real name.</summary>
    private async Task<Exchange> SeedAsync(string status)
    {
        var hat = _hatFaker.Generate();
        await _provider.CreateHatAsync(hat);

        var created = new List<Participant>();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            var request = _participantFaker.Generate() with
            {
                HatId = hat.HatId,
                OrganizerEmail = hat.OrganizerEmail
            };

            created.Add(await _provider.CreateParticipantAsync(request, [.. created]));
        }

        for (var index = 0; index < created.Count; index++)
            await _provider.UpdateParticipantPickedRecipientAsync(
                hat.OrganizerEmail,
                hat.HatId,
                created[index].Person.Email,
                created[(index + 1) % created.Count].Person.Name);

        await _provider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, status);

        return new Exchange
        {
            HatId = hat.HatId,
            OrganizerEmail = hat.OrganizerEmail,
            ParticipantIds = await _provider.GetParticipantIdsByEmailAsync(hat.HatId),
            PickedNames = created
                .Select((participant, index) => (participant, index))
                .ToImmutableDictionary(
                    pair => pair.participant.Person.Email,
                    pair => created[(pair.index + 1) % created.Count].Person.Name)
        };
    }

    private sealed record Exchange
    {
        public required Guid HatId { get; init; }
        public required string OrganizerEmail { get; init; }
        public required ImmutableDictionary<string, Guid> ParticipantIds { get; init; }
        public required ImmutableDictionary<string, string> PickedNames { get; init; }
    }
}
