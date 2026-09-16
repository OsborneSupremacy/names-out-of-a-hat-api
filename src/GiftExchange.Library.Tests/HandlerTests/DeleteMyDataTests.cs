using GiftExchange.Library.Contexts;
using NSubstitute;

namespace GiftExchange.Library.Tests.HandlerTests;

/// <summary>
/// The request half of deleting somebody's data: what is done before answering, and what is queued.
/// The deleting itself is covered by DataDeletionTests.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DeleteMyDataTests
{
    private const string CallerEmail = "leaving.for.good@example.com";

    private readonly ILambdaContext _context = new FakeLambdaContext();

    private readonly IDataDeletionQueue _queue = Substitute.For<IDataDeletionQueue>();

    private readonly List<DataDeletionMessage> _queued = [];

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly JsonService _jsonService;

    private readonly IApiGatewayHandler _sut;

    public DeleteMyDataTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _contextFactory = dbFixture.CreateContextFactory();

        _queue.EnqueueAsync(Arg.Do<DataDeletionMessage>(message => _queued.Add(message)))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddValidators()
            .AddSingleton(_contextFactory)
            .AddSingleton(_queue)
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("delete/profile");
    }

    [Fact]
    public async Task DeleteMyData_ReturnsAcceptedAndQueuesTheCallersDeletion()
    {
        // arrange
        var before = DateTimeOffset.UtcNow;

        // act
        var response = await DeleteAsync(CallerEmail, forgetMe: true, doNotAddAnywhere: false);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Accepted);

        var message = _queued.Should().ContainSingle().Subject;
        message.Email.Should().Be(CallerEmail);
        message.ForgetMe.Should().BeTrue();
        message.RequestedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task DeleteMyData_TakesTheAddressFromTheAuthorizerNotTheBody()
    {
        // act
        var body = """{"organizerEmail":"somebody.else@example.com","forgetMe":false,"doNotAddAnywhere":false}""";
        var response = await _sut.FunctionHandler(body.ToApiGatewayProxyRequest(CallerEmail), _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Accepted);
        _queued.Should().ContainSingle().Which.Email.Should().Be(CallerEmail);
    }

    [Fact]
    public async Task DeleteMyData_WithoutDoNotAddAnywhere_RecordsNoRefusal()
    {
        // arrange
        const string email = "not.refusing@example.com";

        // act
        await DeleteAsync(email, forgetMe: false, doNotAddAnywhere: false);

        // assert
        (await CountRefusalsAsync(email)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteMyData_WithDoNotAddAnywhere_RecordsOneRefusalHoweverOftenItIsAsked()
    {
        // arrange
        const string email = "refusing.everybody@example.com";

        // act
        await DeleteAsync(email, forgetMe: false, doNotAddAnywhere: true);
        await DeleteAsync(email, forgetMe: false, doNotAddAnywhere: true);

        // assert
        (await CountRefusalsAsync(email)).Should().Be(1);

        await using var context = await _contextFactory.CreateDbContextAsync();
        (await context.DoNotAddToExchange.CountAsync(block => block.EmailNormalized == email)).Should().Be(0);
    }

    private Task<APIGatewayProxyResponse> DeleteAsync(string email, bool forgetMe, bool doNotAddAnywhere) =>
        _sut.FunctionHandler(
            _jsonService
                .SerializeDefault(new DeleteMyDataRequest { ForgetMe = forgetMe, DoNotAddAnywhere = doNotAddAnywhere })
                .ToApiGatewayProxyRequest(email),
            _context);

    private async Task<int> CountRefusalsAsync(string email)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.DoNotAddAnywhere.CountAsync(block => block.EmailNormalized == email);
    }
}
