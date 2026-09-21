namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class EditParticipantTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly TestDataService _testDataService;

    private readonly AddParticipantRequestFaker _addParticipantRequestFaker;

    private readonly IApiGatewayHandler _sut;

    public EditParticipantTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _addParticipantRequestFaker = new AddParticipantRequestFaker();

        var contextFactory = dbFixture.CreateContextFactory();
        _context = new FakeLambdaContext();

        IServiceProvider serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .AddSingleton<IContentModerationService, FakeContentModerationService>()
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _testDataService = new TestDataService(serviceProvider.GetRequiredService<GiftExchangeProvider>());

        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("put/participant");
    }

    [Fact]
    public async Task EditParticipant_ValidPayload_OkResponse()
    {
        // arrange
        var participantEmails = new List<string>();

        var hat = await _testDataService.CreateTestHatAsync();

        // add organizer as participant
        await _testDataService.CreateParticipantAsync(new AddParticipantRequest
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id,
            Name = hat.Organizer.Name,
            Email = hat.Organizer.Email
        }, []);

        participantEmails.Add(hat.Organizer.Email);

        // add other participants
        foreach(var otherParticipant in _addParticipantRequestFaker.Generate(5))
        {
            await _testDataService.CreateParticipantAsync(otherParticipant with
            {
                OrganizerEmail = hat.Organizer.Email,
                HatId = hat.Id
            }, []);
            participantEmails.Add(otherParticipant.Email);
        }

        // add participant to be edited
        var participantUt = _addParticipantRequestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        };

        await _testDataService.CreateParticipantAsync(participantUt, []);

        var innerRequest = new EditParticipantRequest
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id,
            Email = participantUt.Email,
            EligibleRecipients = participantEmails.ToImmutableList()
        };

        var apiRequest = _jsonService
            .SerializeDefault(innerRequest)
            .ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(apiRequest, _context);

        var updatedParticipant = await _testDataService.GetParticipantAsync(
            hat.Organizer.Email,
            hat.Id,
            participantUt.Email
        );

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        updatedParticipant.EligibleRecipients.Select(recipient => recipient.Email).Should().BeEquivalentTo(participantEmails);
    }

    /// <summary>
    /// Two people in one exchange may share a name, so eligibility is stated by address. Naming one
    /// of two Sams has to leave the other one out.
    /// </summary>
    [Fact]
    public async Task EditParticipant_GivenTwoParticipantsWithTheSameName_KeepsThemApart()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        var firstSam = _addParticipantRequestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id,
            Name = "Sam"
        };

        var secondSam = _addParticipantRequestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id,
            Name = "Sam"
        };

        var participantUt = _addParticipantRequestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        };

        await _testDataService.CreateParticipantAsync(firstSam, []);
        await _testDataService.CreateParticipantAsync(secondSam, []);
        await _testDataService.CreateParticipantAsync(participantUt, []);

        var apiRequest = _jsonService
            .SerializeDefault(new EditParticipantRequest
            {
                OrganizerEmail = hat.Organizer.Email,
                HatId = hat.Id,
                Email = participantUt.Email,
                EligibleRecipients = [secondSam.Email]
            })
            .ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(apiRequest, _context);

        var updatedParticipant = await _testDataService.GetParticipantAsync(
            hat.Organizer.Email,
            hat.Id,
            participantUt.Email
        );

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        updatedParticipant.EligibleRecipients
            .Should().ContainSingle()
            .Which.Email.Should().Be(secondSam.Email);
    }
}
