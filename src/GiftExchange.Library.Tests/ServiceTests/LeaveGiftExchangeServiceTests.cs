using GiftExchange.Library.Contexts;
using GiftExchange.Library.Utility;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// Leaving a gift exchange, against a real database.
///
/// Three properties carry the weight here, and none of them is visible from the happy path alone:
/// that a GET removes nobody, that nothing sent to the rest of the exchange names the person who
/// left, and that leaving cannot be undone by the organizer adding them back.
///
/// The provider is the real one, for the reason the other service tests give — the ordering of the
/// refusal, the removal and the status change is the behaviour under test, and a substitute would
/// only assert that this class called what it was written to call. The queue is a substitute,
/// because what matters about it is what it was handed.
/// </summary>
[Collection(PostgresCollection.Name)]
public class LeaveGiftExchangeServiceTests
{
    static LeaveGiftExchangeServiceTests()
    {
        DotEnv.Load();
        Environment.SetEnvironmentVariable("LIVE_MODE", "true");
    }

    private readonly IEmailQueue _queue = Substitute.For<IEmailQueue>();

    private readonly List<GiftExchangeEmailRequest> _queued = [];

    private readonly GiftExchangeProvider _provider;

    private readonly DoNotAddService _doNotAddService;

    private readonly LeaveGiftExchangeService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public LeaveGiftExchangeServiceTests(PostgresFixture dbFixture)
    {
        IDbContextFactory<GiftExchangeDbContext> contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _doNotAddService = serviceProvider.GetRequiredService<DoNotAddService>();

        _queue.EnqueueAsync(Arg.Do<GiftExchangeEmailRequest>(email => _queued.Add(email)))
            .Returns(Task.CompletedTask);

        _sut = new LeaveGiftExchangeService(
            _provider,
            new LeavePageComposer(),
            new LeaveEmailCompositionService(),
            _queue,
            Substitute.For<ILogger<LeaveGiftExchangeService>>());
    }

    [Fact]
    public async Task Get_RendersTheFormAndRemovesNobody()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        var response = await _sut.FunctionHandler(Request("GET", exchange.LeaverToken), new FakeLambdaContext());

        // assert: the whole reason this is two endpoints. A mail scanner following the link in an
        // invitation gets this page, and nobody is removed, nobody is emailed, and the exchange is
        // left exactly as it was.
        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("<form method=\"post\"");
        response.Body.Should().Contain("Yes, leave this gift exchange");

        _queued.Should().BeEmpty();
        await ParticipantCountShouldBeAsync(exchange, 4);
        await StatusShouldBeAsync(exchange, HatStatus.InvitationsSent);
    }

    [Fact]
    public async Task Get_OffersBothRefusalsUnticked()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        var response = await _sut.FunctionHandler(Request("GET", exchange.LeaverToken), new FakeLambdaContext());

        // assert: unticked, because these last indefinitely and nobody chose them by arriving here.
        response.Body.Should().Contain($"name=\"{LeavePageComposer.BlockOrganizerField}\"");
        response.Body.Should().Contain($"name=\"{LeavePageComposer.BlockAnywhereField}\"");
        response.Body.Should().NotContain("checked");
    }

    [Fact]
    public async Task Post_AfterInvitationsGoOut_RemovesThemAndSendsTheExchangeBackToTheHat()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        await _sut.FunctionHandler(Request("POST", exchange.LeaverToken), new FakeLambdaContext());

        // assert
        await ParticipantCountShouldBeAsync(exchange, 3);
        await StatusShouldBeAsync(exchange, HatStatus.InProgress);
    }

    [Fact]
    public async Task Post_AfterInvitationsGoOut_TellsEverybodyElseWithoutNamingThem()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        await _sut.FunctionHandler(Request("POST", exchange.LeaverToken), new FakeLambdaContext());

        // assert: the one secret this feature has to keep. Everybody still in the exchange is told,
        // and nothing in what they are told is the leaver's name or address.
        var notices = _queued
            .Where(email => email.MessageType == EmailMessageType.ParticipantLeft)
            .ToImmutableList();

        notices.Should().HaveCount(3, "the three who are still in it, and not the person who left");

        notices.Select(notice => notice.RecipientEmail)
            .Should().NotContain(exchange.LeaverEmail);

        foreach (var notice in notices)
        {
            notice.HtmlBody.Should().NotContain(exchange.LeaverName);
            notice.HtmlBody.Should().NotContain(exchange.LeaverEmail);
            notice.HtmlBody.Should().Contain("disregard the name you were assigned");
            notice.Subject.Should().NotContain("left", "a subject line is read over somebody's shoulder");
        }

        // Composed once, so nothing can vary by recipient and be compared afterwards.
        notices.Select(notice => notice.HtmlBody).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Post_TellsTheOrganizerWhoItWas()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        await _sut.FunctionHandler(Request("POST", exchange.LeaverToken), new FakeLambdaContext());

        // assert: the one person entitled to know, because they cannot run the exchange otherwise.
        var organizerNotice = _queued
            .Should().ContainSingle(email => email.MessageType == EmailMessageType.OrganizerParticipantLeft)
            .Subject;

        organizerNotice.RecipientEmail.Should().Be(exchange.OrganizerEmail);
        organizerNotice.HtmlBody.Should().Contain(exchange.LeaverName);
        organizerNotice.HtmlBody.Should().Contain(exchange.LeaverEmail);
        organizerNotice.HtmlBody.Should().Contain("shake the hat again");
        organizerNotice.HtmlBody.Should().Contain("check with somebody before you add them",
            "the advice is as much the point of this email as the news is");
    }

    [Theory]
    // The wire values rather than the HatStatus members, which are properties and not constants and
    // so cannot appear in an attribute. Worth having spelled out anyway: these two strings are what
    // the database holds, and the whole behaviour under test turns on them.
    [InlineData("READY_TO_CLOSE")]
    [InlineData("CLOSED")]
    public async Task Post_OnceTheExchangeIsOver_RemovesThemWithoutReopeningIt(string status)
    {
        // arrange
        var exchange = await SeedAsync(status);

        // act
        await _sut.FunctionHandler(Request("POST", exchange.LeaverToken), new FakeLambdaContext());

        // assert: leaving still works and the refusal is still recorded, but nobody is sent back to
        // the hat for gifts that have already changed hands, and nobody else is told to disregard a
        // name they have already acted on.
        await ParticipantCountShouldBeAsync(exchange, 3);
        await StatusShouldBeAsync(exchange, status);

        _queued.Should().NotContain(email => email.MessageType == EmailMessageType.ParticipantLeft);
        _queued.Should().ContainSingle(email => email.MessageType == EmailMessageType.OrganizerParticipantLeft);

        (await RefusedAsync(exchange, exchange.LeaverEmail)).Should().BeTrue();
    }

    [Fact]
    public async Task Post_WithNothingTicked_RefusesOnlyThisExchange()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);
        var elsewhere = await SeedAsync(HatStatus.InProgress, exchange.OrganizerEmail);

        // act
        await _sut.FunctionHandler(Request("POST", exchange.LeaverToken), new FakeLambdaContext());

        // assert
        (await RefusedAsync(exchange, exchange.LeaverEmail)).Should().BeTrue();
        (await RefusedAsync(elsewhere, exchange.LeaverEmail)).Should()
            .BeFalse("leaving one exchange is not a statement about the organizer's next one");
    }

    [Fact]
    public async Task Post_WithTheOrganizerBoxTicked_RefusesEveryExchangeOfTheirs()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);
        var elsewhere = await SeedAsync(HatStatus.InProgress, exchange.OrganizerEmail);
        var strangers = await SeedAsync(HatStatus.InProgress);

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.LeaverToken, LeavePageComposer.BlockOrganizerField),
            new FakeLambdaContext());

        // assert
        (await RefusedAsync(elsewhere, exchange.LeaverEmail)).Should().BeTrue();
        (await RefusedAsync(strangers, exchange.LeaverEmail)).Should()
            .BeFalse("refusing one organizer says nothing about anybody else");
    }

    [Fact]
    public async Task Post_WithTheEverythingBoxTicked_RefusesAnOrganizerTheyHaveNeverMet()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);
        var strangers = await SeedAsync(HatStatus.InProgress);

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.LeaverToken, LeavePageComposer.BlockAnywhereField),
            new FakeLambdaContext());

        // assert
        (await RefusedAsync(strangers, exchange.LeaverEmail)).Should().BeTrue();
    }

    [Fact]
    public async Task Post_TheResultPageSaysWhichRefusalsWereRecorded()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.LeaverToken, LeavePageComposer.BlockAnywhereField),
            new FakeLambdaContext());

        // assert: listed rather than counted, so the reader can check the one they meant happened.
        response.Body.Should().Contain("can't be added back to this gift exchange");
        response.Body.Should().Contain("Nobody can add you to a gift exchange");
        response.Body.Should().NotContain("can't add you to any gift exchange",
            "the organizer box was not ticked");
    }

    [Fact]
    public async Task AfterLeaving_TheOrganizerCannotAddThemBack()
    {
        // arrange: the thing this whole feature exists for. The organizer is told somebody left and
        // asked to draw names again, so the participant list is the first thing they open.
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        await _sut.FunctionHandler(Request("POST", exchange.LeaverToken), new FakeLambdaContext());

        // assert
        (await RefusedAsync(exchange, exchange.LeaverEmail.ToUpperInvariant())).Should()
            .BeTrue("retyping the address in a different case is not a different person");
    }

    [Fact]
    public async Task Post_Twice_TheSecondFindsNothingAndActsOnNobody()
    {
        // arrange: two tabs, a double submit, or a scanner that follows a POST. The leave token
        // goes with the participant, so the second submission has nothing to resolve.
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        await _sut.FunctionHandler(Request("POST", exchange.LeaverToken), new FakeLambdaContext());
        _queued.Clear();

        // act
        var second = await _sut.FunctionHandler(Request("POST", exchange.LeaverToken), new FakeLambdaContext());

        // assert
        second.StatusCode.Should().Be(200);
        second.Body.Should().Contain("We can't leave a gift exchange from this link");
        _queued.Should().BeEmpty();
        await ParticipantCountShouldBeAsync(exchange, 3);
    }

    [Fact]
    public async Task AnUnknownToken_ShowsTheSamePageAsASpentOne()
    {
        // act
        var response = await _sut.FunctionHandler(
            Request("GET", "not-a-real-token"),
            new FakeLambdaContext());

        // assert: telling the two apart would let somebody holding a guessed token learn whether it
        // named a real participant — and a token that resolves here removes somebody.
        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("We can't leave a gift exchange from this link");
    }

    [Fact]
    public async Task TheOrganizerIsNeverIssuedALeaveToken()
    {
        // arrange: the enforcement is the absence of a row, not a flag somebody has to check.
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        var tokens = await _provider.IssueLeaveTokensAsync(exchange.HatId);

        // assert
        tokens.Should().NotContainKey(exchange.OrganizerEmail);
        tokens.Should().HaveCount(3, "the three participants who are not running the exchange");
    }

    [Fact]
    public async Task ReissuingInvitations_RetiresTheOldLeaveLink()
    {
        // arrange: invitations can go out more than once, and a link in a superseded invitation
        // would remove somebody from a draw its own email no longer describes.
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        await _provider.IssueLeaveTokensAsync(exchange.HatId);

        var response = await _sut.FunctionHandler(
            Request("GET", exchange.LeaverToken),
            new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("We can't leave a gift exchange from this link");
    }

    private async Task<bool> RefusedAsync(SeededExchange exchange, string email) =>
        await _doNotAddService.IsRefusedAsync(email, exchange.OrganizerEmail, exchange.HatId);

    private async Task ParticipantCountShouldBeAsync(SeededExchange exchange, int expected)
    {
        var (_, hat) = await _provider.GetHatAsync(exchange.OrganizerEmail, exchange.HatId);
        hat.Participants.Should().HaveCount(expected);
    }

    private async Task StatusShouldBeAsync(SeededExchange exchange, string expected)
    {
        var (_, hat) = await _provider.GetHatAsync(exchange.OrganizerEmail, exchange.HatId);
        hat.Status.Should().Be(expected);
    }

    private static APIGatewayProxyRequest Request(string method, string token, params string[] ticked) =>
        new()
        {
            HttpMethod = method,
            Resource = "/leave/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token },
            Body = string.Join("&", ticked.Select(field => $"{field}=yes"))
        };

    /// <summary>
    /// The organizer plus three others in a ring, with leave tokens issued.
    /// </summary>
    /// <remarks>
    /// The organizer is a participant of their own exchange, as <c>CreateHatService</c> makes them,
    /// because half of what these tests check is what happens to somebody who is both — they
    /// receive the anonymous notice and the named one, and they hold no leave token of their own.
    /// </remarks>
    private async Task<SeededExchange> SeedAsync(string status, string organizerEmail = "")
    {
        var hat = _hatFaker.Generate();

        if (!string.IsNullOrWhiteSpace(organizerEmail))
            hat = hat with { OrganizerEmail = organizerEmail };

        await _provider.CreateHatAsync(hat);

        var created = new List<Participant>
        {
            await _provider.CreateParticipantAsync(
                new AddParticipantRequest
                {
                    HatId = hat.HatId,
                    OrganizerEmail = hat.OrganizerEmail,
                    Name = hat.OrganizerName,
                    Email = hat.OrganizerEmail
                },
                [])
        };

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
                created[(index + 1) % created.Count].Person.Email);

        await _provider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, status);

        var tokens = await _provider.IssueLeaveTokensAsync(hat.HatId);
        var leaver = created[1];

        return new SeededExchange
        {
            HatId = hat.HatId,
            OrganizerEmail = hat.OrganizerEmail,
            LeaverEmail = leaver.Person.Email,
            LeaverName = leaver.Person.Name,
            LeaverToken = tokens[leaver.Person.Email]
        };
    }

    private sealed record SeededExchange
    {
        public required Guid HatId { get; init; }
        public required string OrganizerEmail { get; init; }
        public required string LeaverEmail { get; init; }
        public required string LeaverName { get; init; }
        public required string LeaverToken { get; init; }
    }
}
