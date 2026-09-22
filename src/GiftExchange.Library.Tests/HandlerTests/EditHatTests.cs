using GiftExchange.Library.Contexts;

namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class EditHatTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly TestDataService _testDataService;

    private readonly GiftExchangeProvider _provider;

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly IApiGatewayHandler _sut;

    public EditHatTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _contextFactory = dbFixture.CreateContextFactory();
        _context = new FakeLambdaContext();

        IServiceProvider serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            // The far bound on the date is the validator's, so this suite needs them where most
            // handler suites do without.
            .AddValidators()
            .AddSingleton(_contextFactory)
            .AddSingleton<IContentModerationService, FakeContentModerationService>()
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _testDataService = new TestDataService(_provider);

        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("put/hat");
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task EditHat_ValidRequest_OkResponse()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        var editHatRequest = Edit(hat) with
        {
            Name = "New Hat Name",
            AdditionalInformation = "New Additional Information",
            PriceRange = "$20 - $30",
            ExchangeDate = Today.AddMonths(2)
        };

        // act
        var response = await SendAsync(editHatRequest);
        var updatedHat = await _testDataService
            .GetHatAsync(editHatRequest.OrganizerEmail, hat.Id);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        updatedHat.Name.Should().Be(editHatRequest.Name);
        updatedHat.AdditionalInformation.Should().Be(editHatRequest.AdditionalInformation);
        updatedHat.PriceRange.Should().Be(editHatRequest.PriceRange);
        updatedHat.ExchangeDate.Should().Be(editHatRequest.ExchangeDate);
    }

    [Fact]
    public async Task EditHat_MinimumDate_ClearsTheDate()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();
        (await SendAsync(Edit(hat) with { ExchangeDate = Today.AddMonths(1) })).StatusCode.Should().Be((int)HttpStatusCode.OK);

        // act
        var response = await SendAsync(Edit(hat) with { ExchangeDate = DateOnly.MinValue });

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        (await _testDataService.GetHatAsync(hat.Organizer.Email, hat.Id)).ExchangeDate.Should().Be(DateOnly.MinValue);
    }

    [Fact]
    public async Task EditHat_NewDateInThePast_IsRefused()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        // act
        var response = await SendAsync(Edit(hat) with { ExchangeDate = Today.AddDays(-5) });

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        (await _testDataService.GetHatAsync(hat.Organizer.Email, hat.Id)).ExchangeDate.Should().Be(DateOnly.MinValue);
    }

    /// <summary>
    /// A date that was fine when it was set and has since passed must not stop the organizer
    /// editing everything else about the exchange.
    /// </summary>
    [Fact]
    public async Task EditHat_ExistingDateInThePast_CanBeKept()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();
        var passed = Today.AddDays(-5);
        await SetExchangeDateAsync(hat.Id, passed);

        // act
        var response = await SendAsync(Edit(hat) with { PriceRange = "$10", ExchangeDate = passed });

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var updated = await _testDataService.GetHatAsync(hat.Organizer.Email, hat.Id);
        updated.PriceRange.Should().Be("$10");
        updated.ExchangeDate.Should().Be(passed);
    }

    [Fact]
    public async Task EditHat_DateMoreThanTwoYearsAway_IsRefused()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();

        // act
        var response = await SendAsync(Edit(hat) with { ExchangeDate = Today.AddYears(2).AddDays(2) });

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A moved date is a new date, so the prompt measured from it has not been sent yet. An edit
    /// that leaves the date alone must not reset it, or saving the price range could prompt twice.
    /// </summary>
    [Fact]
    public async Task EditHat_MovingTheDate_ResetsTheClosePrompt_AndKeepingItDoesNot()
    {
        // arrange
        var hat = await _testDataService.CreateTestHatAsync();
        var date = Today.AddMonths(1);
        (await SendAsync(Edit(hat) with { ExchangeDate = date })).StatusCode.Should().Be((int)HttpStatusCode.OK);
        var promptedAt = DateTimeOffset.UtcNow;
        (await _provider.TryClaimClosePromptAsync(hat.Id, promptedAt)).Should().BeTrue();

        // act: an unrelated edit
        (await SendAsync(Edit(hat) with { PriceRange = "$15", ExchangeDate = date })).StatusCode.Should().Be((int)HttpStatusCode.OK);

        // assert
        (await ClosePromptSentAtAsync(hat.Id)).Should().BeCloseTo(promptedAt, TimeSpan.FromMilliseconds(1));

        // act: the date moves
        (await SendAsync(Edit(hat) with { ExchangeDate = date.AddDays(3) })).StatusCode.Should().Be((int)HttpStatusCode.OK);

        // assert
        (await ClosePromptSentAtAsync(hat.Id)).Should().Be(DateTimeOffset.MinValue);
    }

    /// <summary>
    /// An edit that changes nothing but what a test sets on top of it.
    /// </summary>
    /// <remarks>
    /// The price range and additional information are fixed rather than carried over from the
    /// faked hat. Faked words can carry punctuation the validators refuse, which would fail an
    /// arranging edit at random and leave the test asserting about a date that was never saved.
    /// </remarks>
    private static EditHatRequest Edit(Hat hat) => new()
    {
        OrganizerEmail = hat.Organizer.Email,
        HatId = hat.Id,
        Name = hat.Name,
        AdditionalInformation = "Bring a card.",
        PriceRange = "$25",
        ExchangeDate = hat.ExchangeDate
    };

    private Task<APIGatewayProxyResponse> SendAsync(EditHatRequest request) =>
        _sut.FunctionHandler(_jsonService.SerializeDefault(request).ToApiGatewayProxyRequest(), _context);

    private async Task SetExchangeDateAsync(Guid hatId, DateOnly date)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Hats
            .Where(hat => hat.HatId == hatId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(hat => hat.ExchangeDate, date));
    }

    private async Task<DateTimeOffset> ClosePromptSentAtAsync(Guid hatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Hats
            .Where(hat => hat.HatId == hatId)
            .Select(hat => hat.ClosePromptSentAt)
            .SingleAsync();
    }
}
