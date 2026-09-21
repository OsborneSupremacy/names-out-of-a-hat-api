using GiftExchange.Library.Contexts;

namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class CreateHatTests
{
    private readonly JsonService _jsonService;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly ILambdaContext _context;

    private readonly CreateHatRequestFaker _requestFaker;

    private readonly IApiGatewayHandler _sut;

    public CreateHatTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _requestFaker = new CreateHatRequestFaker();

        _contextFactory = dbFixture.CreateContextFactory();
        _context = new FakeLambdaContext();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .AddSingleton<IContentModerationService, FakeContentModerationService>()
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _giftExchangeProvider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("post/hat");
    }

    [Fact]
    public async Task CreateHat_ValidRequest_CreatedResponse()
    {
        // arrange
        var request = _jsonService
            .SerializeDefault(_requestFaker.Generate())
            .ToApiGatewayProxyRequest();

        // act
        var response = await _sut.FunctionHandler(request, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateHat_HatAlreadyExists_ConflictResponse()
    {
        // arrange
        var request = _jsonService
            .SerializeDefault(_requestFaker.Generate())
            .ToApiGatewayProxyRequest();

        // act
        _ = await _sut.FunctionHandler(request, _context);
        var response = await _sut.FunctionHandler(request, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }

    /// <summary>
    /// The allowance is per organizer, so a whole day's worth from one address spends it and the
    /// next one has to be refused. Everything up to the limit is asserted too: a limit that also
    /// refused the fifth would pass a test that only looked at the sixth.
    /// </summary>
    /// <remarks>
    /// Each is closed as it is made, which is what an organizer walking around the open limit would
    /// do, and what keeps that limit from answering first.
    /// </remarks>
    [Fact]
    public async Task CreateHat_DailyLimitSpent_TooManyRequestsResponse()
    {
        // arrange
        var organizer = _requestFaker.Generate();

        for (var created = 0; created < HatCreationLimiter.DailyLimit; created++)
            await CloseAsync(organizer, await CreateAllowedAsync(organizer));

        // act
        var response = await _sut.FunctionHandler(AnotherHatFor(organizer), _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Open exchanges are counted regardless of when they were made, so these are moved out of the
    /// daily window first — otherwise the sixth would be refused for the other limit, and the test
    /// would pass without this one existing.
    /// </summary>
    [Fact]
    public async Task CreateHat_OpenLimitReached_ConflictResponse()
    {
        // arrange
        var organizer = _requestFaker.Generate();

        for (var created = 0; created < HatCreationLimiter.OpenLimit; created++)
            await CreateAllowedAsync(organizer);

        await MoveOutOfTheDailyWindowAsync(organizer);

        // act
        var response = await _sut.FunctionHandler(AnotherHatFor(organizer), _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }

    /// <summary>A closed exchange no longer counts, so closing one makes room straight away.</summary>
    [Fact]
    public async Task CreateHat_OpenLimitReachedThenOneClosed_CreatedResponse()
    {
        // arrange
        var organizer = _requestFaker.Generate();

        var hatIds = new List<Guid>();

        for (var created = 0; created < HatCreationLimiter.OpenLimit; created++)
            hatIds.Add(await CreateAllowedAsync(organizer));

        await MoveOutOfTheDailyWindowAsync(organizer);
        await CloseAsync(organizer, hatIds[0]);

        // act
        var response = await _sut.FunctionHandler(AnotherHatFor(organizer), _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Created);
    }

    /// <summary>Creates the organizer's next exchange, asserting that it was allowed.</summary>
    private async Task<Guid> CreateAllowedAsync(CreateHatRequest organizer)
    {
        var response = await _sut.FunctionHandler(AnotherHatFor(organizer), _context);
        response.StatusCode.Should().Be((int)HttpStatusCode.Created);

        return _jsonService.DeserializeDefault<CreateHatResponse>(response.Body)!.HatId;
    }

    private Task CloseAsync(CreateHatRequest organizer, Guid hatId) =>
        _giftExchangeProvider.UpdateHatStatusAsync(organizer.OrganizerEmail, hatId, HatStatus.Closed);

    /// <summary>Makes everything this organizer has created look two days old.</summary>
    private async Task MoveOutOfTheDailyWindowAsync(CreateHatRequest organizer)
    {
        await using var context = _contextFactory.CreateDbContext();

        var organizerPersonIds = context.Persons
            .Where(person => person.Email == organizer.OrganizerEmail)
            .Select(person => person.PersonId);

        await context.Hats
            .Where(hat => organizerPersonIds.Contains(hat.OrganizerPersonId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(hat => hat.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)));
    }

    /// <summary>
    /// The organizer's next exchange: a freshly faked one carrying their name and address, so it
    /// differs from the last only in the ways that do not matter to the limit.
    /// </summary>
    private APIGatewayProxyRequest AnotherHatFor(CreateHatRequest organizer) =>
        _jsonService
            .SerializeDefault(_requestFaker.Generate() with
            {
                OrganizerName = organizer.OrganizerName,
                OrganizerEmail = organizer.OrganizerEmail
            })
            .ToApiGatewayProxyRequest();
}
