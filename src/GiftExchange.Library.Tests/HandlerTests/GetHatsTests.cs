namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class GetHatsTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly TestDataService _testDataService;

    private readonly HatDataModelFaker _hatDataModelFaker;

    private readonly IApiGatewayHandler _sut;

    public GetHatsTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _hatDataModelFaker = new HatDataModelFaker();

        var contextFactory = dbFixture.CreateContextFactory();
        _context = new FakeLambdaContext();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddValidators()
            .AddSingleton(contextFactory)
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _testDataService = new TestDataService(serviceProvider.GetRequiredService<GiftExchangeProvider>());

        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("get/hats/{email}");
    }

    [Fact]
    public async Task GetHats_ValidRequest_HatsReturned()
    {
        // arrange
        var hatOne = _hatDataModelFaker.Generate();

        var hatTwo = _hatDataModelFaker.Generate() with
        {
            OrganizerEmail = hatOne.OrganizerEmail,
            OrganizerName = hatOne.OrganizerName
        };

        await Task
            .WhenAll(_testDataService.CreateHatAsync(hatOne), _testDataService.CreateHatAsync(hatTwo));

        var request = new APIGatewayProxyRequest()
            .WithAuthenticatedEmail(hatOne.OrganizerEmail);

        // act
        var response = await _sut.FunctionHandler(request, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var getHatsResponse = _jsonService.DeserializeDefault<GetHatsResponse>(response.Body);
        getHatsResponse!.Hats.Count.Should().Be(2);
        getHatsResponse.OrganizerName.Should().Be(hatOne.OrganizerName);
        getHatsResponse.Page.Should().Be(1);
        getHatsResponse.PageSize.Should().Be(5);
        getHatsResponse.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetHats_GivenOrganizerWithNoHats_ReturnsEmptyOrganizerName()
    {
        // arrange: nobody has created a hat for this address, so there is no name to read back and
        // the UI has to ask for one.
        var request = new APIGatewayProxyRequest()
            .WithAuthenticatedEmail("no.hats.yet@example.com");

        // act
        var response = await _sut.FunctionHandler(request, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var getHatsResponse = _jsonService.DeserializeDefault<GetHatsResponse>(response.Body);
        getHatsResponse!.Hats.Should().BeEmpty();
        getHatsResponse.OrganizerName.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHats_GivenMoreHatsThanAPage_ReturnsOnePageNewestFirst()
    {
        // arrange: created one after another so that each is newer than the last.
        var first = _hatDataModelFaker.Generate();
        var hats = new List<HatDataModel> { first };
        hats.AddRange(_hatDataModelFaker.Generate(6)
            .Select(hat => hat with { OrganizerEmail = first.OrganizerEmail, OrganizerName = first.OrganizerName }));

        foreach (var hat in hats)
            await _testDataService.CreateHatAsync(hat);

        var newestFirst = hats.Select(hat => hat.HatId).Reverse().ToList();

        // act
        var pageOne = await GetPageAsync(first.OrganizerEmail, null);
        var pageTwo = await GetPageAsync(first.OrganizerEmail, "2");
        var pageThree = await GetPageAsync(first.OrganizerEmail, "3");

        // assert
        pageOne.Hats.Select(hat => hat.HatId).Should().Equal(newestFirst.Take(5));
        pageTwo.Hats.Select(hat => hat.HatId).Should().Equal(newestFirst.Skip(5));
        pageTwo.Page.Should().Be(2);
        pageThree.Hats.Should().BeEmpty();
        new[] { pageOne, pageTwo, pageThree }.Should().AllSatisfy(page => page.TotalCount.Should().Be(7));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task GetHats_GivenAnInvalidPage_ReturnsBadRequest(string page)
    {
        // arrange
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string> { { "page", page } }
        }.WithAuthenticatedEmail("bad.page@example.com");

        // act
        var response = await _sut.FunctionHandler(request, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    private async Task<GetHatsResponse> GetPageAsync(string organizerEmail, string? page)
    {
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = page is null ? null : new Dictionary<string, string> { { "page", page } }
        }.WithAuthenticatedEmail(organizerEmail);

        var response = await _sut.FunctionHandler(request, _context);

        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        return _jsonService.DeserializeDefault<GetHatsResponse>(response.Body)!;
    }
}
