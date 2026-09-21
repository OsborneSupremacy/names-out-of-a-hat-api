namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class AddParticipantTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly TestDataService _testDataService;

    private readonly AddParticipantRequestFaker _requestFaker;

    private readonly IApiGatewayHandler _sut;

    private readonly GiftExchangeProvider _provider;

    public AddParticipantTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        var contextFactory = dbFixture.CreateContextFactory();
        _context = new FakeLambdaContext();

        IServiceProvider serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .AddSingleton<IContentModerationService, FakeContentModerationService>()
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _testDataService = new TestDataService(_provider);

        _requestFaker = new AddParticipantRequestFaker();

        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("post/participant");
    }

    [Fact]
    public async Task AddParticipant_ValidRequest_CreatedResponse()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        var request = _jsonService.SerializeDefault(_requestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        }).ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(request, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Created);
    }

    [Fact]
    public async Task AddParticipant_SameEmailAttempt_ConflictResponse()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        var innerRequest = _requestFaker.Generate();

        var requestOne =_jsonService.SerializeDefault(innerRequest with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        }).ToApiGatewayProxyRequest();

        var requestTwo = _jsonService.SerializeDefault(_requestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id,
            Email = innerRequest.Email
        }).ToApiGatewayProxyRequest();

        // act
        await _sut.FunctionHandler(requestOne, _context);
        var response = await _sut.FunctionHandler(requestTwo, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }

    [Theory]
    // The three lists, each on its own. Every one of them has to reach this endpoint, and a check
    // that consulted two of the three would look identical from here for most of the year.
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AddParticipant_SomebodyWhoHasRefused_ForbiddenResponse(
        bool blockOrganizer,
        bool blockAnywhere
    )
    {
        // arrange: whichever list they are on, the organizer typing the address back in is the
        // thing that must not work. For the second and third cases the refusal was recorded against
        // a different exchange entirely.
        var refusedIn = await _testDataService.CreateTestHatAsync();
        var hat = blockOrganizer || blockAnywhere
            ? await _testDataService.CreateTestHatAsync()
            : refusedIn;

        var innerRequest = _requestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        };

        await _provider.RecordDoNotAddAsync(new RecordDoNotAddRequest
        {
            Email = innerRequest.Email,
            HatId = refusedIn.Id,
            OrganizerEmail = blockOrganizer ? hat.Organizer.Email : refusedIn.Organizer.Email,
            BlockOrganizer = blockOrganizer,
            BlockAnywhere = blockAnywhere
        });

        var request = _jsonService.SerializeDefault(innerRequest).ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(request, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Forbidden);

        // Forbidden rather than Conflict: a conflict says the request collided with something and
        // could be retried differently, and this one cannot be retried at all with this address.
        response.Body.Should().Contain(DoNotAddService.RefusalMessage);
    }

    [Fact]
    public async Task AddParticipant_TheRefusalMessageSaysNothingAboutWhichListItWas()
    {
        // arrange: an organizer who could tell "they blocked you" from "they blocked everybody"
        // could learn, by typing an address into a new exchange, the fact the person withheld.
        var hat = await _testDataService.CreateTestHatAsync();

        var innerRequest = _requestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        };

        await _provider.RecordDoNotAddAsync(new RecordDoNotAddRequest
        {
            Email = innerRequest.Email,
            HatId = hat.Id,
            OrganizerEmail = hat.Organizer.Email,
            BlockOrganizer = false,
            BlockAnywhere = true
        });

        // act
        var response = await _sut.FunctionHandler(
            _jsonService.SerializeDefault(innerRequest).ToApiGatewayProxyRequest(),
            _context);

        // assert
        response.Body.Should().NotContain("anywhere");
        response.Body.Should().NotContain("organizer");
        response.Body.Should().NotContain("exchange.", "even the scope of the refusal is a fact about it");
    }

    /// <summary>
    /// Two people in one exchange may share a name. A person is one row shared by every exchange
    /// they are in, so refusing a second Sam here would be refusing something about somebody else's
    /// exchanges too. The address is what tells them apart.
    /// </summary>
    [Fact]
    public async Task AddParticipant_SameNameDifferentEmail_IsAccepted()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        var innerRequest = _requestFaker.Generate();

        var requestOne = _jsonService.SerializeDefault(innerRequest with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        }).ToApiGatewayProxyRequest();

        var requestTwo = _jsonService.SerializeDefault(_requestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id,
            Name = innerRequest.Name
        }).ToApiGatewayProxyRequest();

        // act
        await _sut.FunctionHandler(requestOne, _context);
        var response = await _sut.FunctionHandler(requestTwo, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Created);

        var stored = await _testDataService.GetHatAsync(hat.Organizer.Email, hat.Id);
        stored.Participants.Where(participant => participant.Person.Name == innerRequest.Name).Should().HaveCount(2);
    }

    /// <summary>
    /// An exchange holds at most <see cref="ParticipantLimit.MaxParticipants"/> people. Conflict
    /// rather than TooManyRequests: nothing is rate limited, and the way out is removing somebody.
    /// </summary>
    [Fact]
    public async Task AddParticipant_GivenTheHatIsFull_ConflictResponse()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();
        await FillHatAsync(hat, ParticipantLimit.MaxParticipants);

        var request = _jsonService.SerializeDefault(_requestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        }).ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(request, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        response.Body.Should().Contain($"{ParticipantLimit.MaxParticipants} participants");
    }

    /// <summary>
    /// The boundary from the other side: the seat that takes the exchange up to the limit is still
    /// one an organizer is allowed to fill.
    /// </summary>
    [Fact]
    public async Task AddParticipant_GivenOneSeatLeft_CreatedResponse()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();
        await FillHatAsync(hat, ParticipantLimit.MaxParticipants - 1);

        var request = _jsonService.SerializeDefault(_requestFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        }).ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(request, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Created);
    }

    /// <summary>
    /// Seeds a hat straight through the provider rather than through the handler under test. Only
    /// the count matters to these two, and going the long way round would write an eligibility row
    /// for every pair -- most of a thousand of them -- to establish something the handler reads as
    /// a single number.
    /// </summary>
    private async Task FillHatAsync(Hat hat, int participants)
    {
        var existing = ImmutableList<Participant>.Empty;

        for (var seat = 0; seat < participants; seat++)
        {
            var participant = await _testDataService.CreateParticipantAsync(
                _requestFaker.Generate() with { OrganizerEmail = hat.Organizer.Email, HatId = hat.Id },
                existing);

            existing = existing.Add(participant);
        }
    }
}
