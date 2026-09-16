using GiftExchange.Library.Contexts;

namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class DeleteHatTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly TestDataService _testDataService;

    private readonly AddParticipantRequestFaker _participantFaker;

    private readonly IApiGatewayHandler _sut;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    public DeleteHatTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _participantFaker = new AddParticipantRequestFaker();

        var contextFactory = dbFixture.CreateContextFactory();
        _contextFactory = contextFactory;
        _context = new FakeLambdaContext();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _testDataService = new TestDataService(serviceProvider.GetRequiredService<GiftExchangeProvider>());
        _giftExchangeProvider = serviceProvider.GetRequiredService<GiftExchangeProvider>();

        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("delete/hat");
    }

    [Fact]
    public async Task DeleteHat_GivenValidRequest_ReturnsNoContentAndRemovesHat()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        var participantRequest = _participantFaker.Generate() with
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        };

        await _testDataService.CreateParticipantAsync(participantRequest, []);

        var deleteRequest = new DeleteHatRequest
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id
        };

        var apiRequest = _jsonService
            .SerializeDefault(deleteRequest)
            .ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(apiRequest, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NoContent);

        var (exists, _) = await _giftExchangeProvider.GetHatAsync(hat.Organizer.Email, hat.Id);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteHat_RemovesRefusalsOfTheDeletedExchange()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        await _giftExchangeProvider.RecordDoNotAddAsync(new RecordDoNotAddRequest
        {
            Email = "left.this.one@example.com",
            HatId = hat.Id,
            OrganizerEmail = hat.Organizer.Email,
            BlockOrganizer = false,
            BlockAnywhere = false
        });

        var apiRequest = _jsonService
            .SerializeDefault(new DeleteHatRequest { OrganizerEmail = hat.Organizer.Email, HatId = hat.Id })
            .ToApiGatewayProxyRequest();

        // act
        await _sut.FunctionHandler(apiRequest, _context);

        // assert
        await using var context = await _contextFactory.CreateDbContextAsync();
        (await context.DoNotAddToExchange.CountAsync(block => block.HatId == hat.Id)).Should().Be(0);
    }
}
