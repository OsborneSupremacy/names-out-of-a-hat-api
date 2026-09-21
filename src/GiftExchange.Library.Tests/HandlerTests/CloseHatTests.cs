using NSubstitute;

namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class CloseHatTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly TestDataService _testDataService;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly IApiGatewayHandler _sut;

    /// <summary>
    /// Closing now mails every participant, so the queue is part of what this handler does rather
    /// than a detail of it.
    /// </summary>
    private readonly IEmailQueue _emailQueue = Substitute.For<IEmailQueue>();

    public CloseHatTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        var contextFactory = dbFixture.CreateContextFactory();
        _context = new FakeLambdaContext();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .AddSingleton<IContentModerationService, FakeContentModerationService>()
            .AddSingleton(_emailQueue)
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _giftExchangeProvider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _testDataService = new TestDataService(_giftExchangeProvider);
        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("post/hat/close");
    }

    [Fact]
    public async Task CloseHat_GivenCooledOffStatus_ReturnsOkResponse()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        await _giftExchangeProvider
            .UpdateHatStatusAsync(hat.Organizer.Email, hat.Id, HatStatus.CooledOff);

        var innerRequest = new CloseHatRequest
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        };

        var apiRequest = _jsonService
            .SerializeDefault(innerRequest)
            .ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(apiRequest, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var updatedHat = await _testDataService
            .GetHatAsync(hat.Organizer.Email, hat.Id);

        updatedHat.Status.Should().Be(HatStatus.Closed);
    }

    [Fact]
    public async Task CloseHat_GivenInvitationsSentStatus_ReturnsConflictResponse()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        await _giftExchangeProvider
            .UpdateHatStatusAsync(hat.Organizer.Email, hat.Id, HatStatus.InvitationsSent);

        var innerRequest = new CloseHatRequest
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        };

        var apiRequest = _jsonService
            .SerializeDefault(innerRequest)
            .ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(apiRequest, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CloseHat_GivenInProgressStatus_ReturnsConflictResponse()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        var innerRequest = new CloseHatRequest
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        };

        var apiRequest = _jsonService
            .SerializeDefault(innerRequest)
            .ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(apiRequest, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CloseHat_QueuesACompletionEmailForEveryParticipant()
    {
        // arrange
        var hat = await CreateCooledOffHatAsync();

        // act
        var response = await CloseAsync(hat);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var queued = QueuedEmails();

        queued.Select(email => email.RecipientEmail)
            .Should().BeEquivalentTo(hat.Participants.Select(participant => participant.Person.Email));

        queued.Should().AllSatisfy(email =>
        {
            email.HatId.Should().Be(hat.Id);
            email.OrganizerEmail.Should().Be(hat.Organizer.Email);
            email.Subject.Should().Be($"The gift exchange, {hat.Name}, has finished");
        });
    }

    [Fact]
    public async Task CloseHat_GivesEveryParticipantTheWholeDraw()
    {
        // arrange
        var hat = await CreateCooledOffHatAsync();

        // act
        await CloseAsync(hat);

        // assert: the picks are no longer secret, which is the whole point of revealing them, so
        // each participant is told every pairing rather than only their own.
        QueuedEmails().Should().AllSatisfy(email =>
        {
            foreach (var participant in hat.Participants)
                email.HtmlBody.Should().Contain(participant.Person.Name)
                    .And.Contain(participant.PickedRecipient.Name);
        });
    }

    [Fact]
    public async Task CloseHat_GivenTheWrongStatus_QueuesNothing()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        // act
        var response = await CloseAsync(hat);

        // assert: a refused close is not a half-finished one. Nobody is told an exchange has
        // ended that has not.
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        QueuedEmails().Should().BeEmpty();
    }

    /// <summary>
    /// A hat with two participants who have drawn each other, sitting at the only status from
    /// which it can be closed.
    /// </summary>
    private async Task<Hat> CreateCooledOffHatAsync()
    {
        var hat = await _testDataService.CreateTestHatAsync();

        var alice = await AddParticipantAsync(hat, "Alice", "alice@example.com", []);
        var bob = await AddParticipantAsync(hat, "Bob", "bob@example.com", [alice]);

        await _giftExchangeProvider
            .UpdateParticipantPickedRecipientAsync(hat.Organizer.Email, hat.Id, alice.Person.Email, bob.Person.Email);

        await _giftExchangeProvider
            .UpdateParticipantPickedRecipientAsync(hat.Organizer.Email, hat.Id, bob.Person.Email, alice.Person.Email);

        await _giftExchangeProvider
            .UpdateHatStatusAsync(hat.Organizer.Email, hat.Id, HatStatus.CooledOff);

        return await _testDataService.GetHatAsync(hat.Organizer.Email, hat.Id);
    }

    private Task<Participant> AddParticipantAsync(
        Hat hat,
        string name,
        string email,
        ImmutableList<Participant> existingParticipants
    ) =>
        _testDataService.CreateParticipantAsync(
            new AddParticipantRequest
            {
                OrganizerEmail = hat.Organizer.Email,
                HatId = hat.Id,
                Name = name,
                Email = email
            },
            existingParticipants);

    private Task<APIGatewayProxyResponse> CloseAsync(Hat hat)
    {
        var apiRequest = _jsonService
            .SerializeDefault(new CloseHatRequest
            {
                OrganizerEmail = hat.Organizer.Email,
                HatId = hat.Id
            })
            .ToApiGatewayProxyRequest();

        return _sut.FunctionHandler(apiRequest, _context);
    }

    private List<GiftExchangeEmailRequest> QueuedEmails() =>
        _emailQueue.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEmailQueue.EnqueueAsync))
            .Select(call => (GiftExchangeEmailRequest)call.GetArguments()[0]!)
            .ToList();
}
