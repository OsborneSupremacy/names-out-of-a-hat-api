namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class ResetHatTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly HatDataModelFaker _hatDataModelFaker;

    private readonly AddParticipantRequestFaker _addParticipantRequestFaker;

    private readonly IApiGatewayHandler _sut;

    public ResetHatTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _hatDataModelFaker = new HatDataModelFaker();
        _addParticipantRequestFaker = new AddParticipantRequestFaker();

        var contextFactory = dbFixture.CreateContextFactory();
        _context = new FakeLambdaContext();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .AddSingleton<IContentModerationService, FakeContentModerationService>()
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _giftExchangeProvider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("post/hat/reset");
    }

    [Fact]
    public async Task ResetHat_KeepsEverybodyWhoWasInTheExchange()
    {
        // arrange: the people are what a reset keeps, and the part that took work to type in.
        var source = await CreateNarrowedHatAsync();

        // act
        var statusCode = await ResetAsync(source);

        // assert
        statusCode.Should().Be(HttpStatusCode.OK);

        var reread = await GetAsync(source);

        reread.Participants
            .Select(participant => participant.Person)
            .Should()
            .BeEquivalentTo(source.Participants.Select(participant => participant.Person));
    }

    [Fact]
    public async Task ResetHat_MakesEverybodyEligibleForEverybodyElse()
    {
        // arrange: Beta may draw only Alpha, and Charlie may draw only Beta.
        var source = await CreateNarrowedHatAsync();

        // act
        await ResetAsync(source);

        // assert
        var reread = await GetAsync(source);

        foreach (var participant in reread.Participants)
            participant.EligibleRecipients
                .Should()
                .BeEquivalentTo(reread.Participants
                    .Select(other => other.Person)
                    .Where(other => other.Email != participant.Person.Email));
    }

    /// <summary>
    /// Nobody may draw themselves, which is the one rule a reset does not hand back.
    /// </summary>
    [Fact]
    public async Task ResetHat_DoesNotMakeAnybodyEligibleForThemselves()
    {
        // arrange
        var source = await CreateNarrowedHatAsync();

        // act
        await ResetAsync(source);

        // assert
        var reread = await GetAsync(source);

        reread.Participants.Should().OnlyContain(participant =>
            !participant.EligibleRecipients.Any(recipient => recipient.Email == participant.Person.Email));
    }

    [Fact]
    public async Task ResetHat_ThrowsTheDrawAway()
    {
        // arrange
        var source = await CreateNarrowedHatAsync();
        await ShakeAsync(source);

        // act
        await ResetAsync(source);

        // assert: read from the provider rather than through GetHatService, which would redact a
        // surviving pick and make this pass whether or not the draw was actually cleared.
        var reread = await GetAsync(source);

        reread.Participants.Should().OnlyContain(participant => participant.PickedRecipient.Email == string.Empty);
    }

    [Fact]
    public async Task ResetHat_PutsTheExchangeBackToTheBeginning()
    {
        // arrange
        var source = await CreateNarrowedHatAsync();
        await ShakeAsync(source);

        // act
        await ResetAsync(source);

        // assert
        var reread = await GetAsync(source);

        reread.Status.Should().Be(HatStatus.InProgress);
    }

    [Fact]
    public async Task ResetHat_LeavesTheDetailsTheOrganizerTypedAlone()
    {
        // arrange: a reset is not a delete. What it throws away is the setup, not the exchange.
        var source = await CreateNarrowedHatAsync();

        // act
        await ResetAsync(source);

        // assert
        var reread = await GetAsync(source);

        reread.Name.Should().Be(source.Hat.HatName);
        reread.AdditionalInformation.Should().Be(source.Hat.AdditionalInformation);
        reread.PriceRange.Should().Be(source.Hat.PriceRange);
    }

    [Theory]
    [InlineData("IN_PROGRESS")]
    [InlineData("READY_FOR_ASSIGNMENT")]
    [InlineData("NAMES_ASSIGNED")]
    public async Task ResetHat_BeforeInvitationsGoOut_IsAllowed(string status)
    {
        // arrange
        var source = await CreateNarrowedHatAsync();
        await _giftExchangeProvider.UpdateHatStatusAsync(source.Hat.OrganizerEmail, source.Hat.HatId, status);

        // act
        var statusCode = await ResetAsync(source);

        // assert
        statusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Once invitations are out, people have been told who they drew. Undoing the draw would make
    /// what they were told wrong, with no way to tell them so.
    /// </summary>
    [Theory]
    [InlineData("INVITATIONS_SENT")]
    [InlineData("READY_TO_CLOSE")]
    [InlineData("CLOSED")]
    public async Task ResetHat_OnceInvitationsHaveGoneOut_ReturnsConflictAndChangesNothing(string status)
    {
        // arrange
        var source = await CreateNarrowedHatAsync();
        await ShakeAsync(source);
        await _giftExchangeProvider.UpdateHatStatusAsync(source.Hat.OrganizerEmail, source.Hat.HatId, status);

        var before = await GetAsync(source);

        // act
        var statusCode = await ResetAsync(source);

        // assert
        statusCode.Should().Be(HttpStatusCode.Conflict);

        var after = await GetAsync(source);

        after.Status.Should().Be(status);
        after.Participants.Should().OnlyContain(participant => participant.PickedRecipient.Email != string.Empty);
        EligibilityIn(after).Should().BeEquivalentTo(EligibilityIn(before));
    }

    [Fact]
    public async Task ResetHat_GivenSomebodyElsesHat_ReturnsNotFoundAndChangesNothing()
    {
        // arrange
        var source = await CreateNarrowedHatAsync();

        var body = _jsonService.SerializeDefault(new ResetHatRequest
        {
            OrganizerEmail = source.Hat.OrganizerEmail,
            HatId = source.Hat.HatId
        });

        // act: the body names the owner, but the authenticated caller is somebody else.
        var response = await _sut.FunctionHandler(
            body.ToApiGatewayProxyRequest("intruder@example.com"),
            _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);

        EligibilityIn(await GetAsync(source)).Should().BeEquivalentTo(EligibilityIn(source.Participants));
    }

    /// <summary>
    /// Three participants with the eligibility narrowed by hand, which is the state a reset exists
    /// to undo.
    /// </summary>
    private async Task<SourceHat> CreateNarrowedHatAsync()
    {
        var hat = _hatDataModelFaker.Generate();
        await _giftExchangeProvider.CreateHatAsync(hat);

        var alpha = await AddParticipantAsync(hat, []);
        var beta = await AddParticipantAsync(hat, [alpha]);
        var charlie = await AddParticipantAsync(hat, [alpha, beta]);

        await _giftExchangeProvider.UpdateEligibleRecipientsAsync(
            hat.OrganizerEmail, hat.HatId, beta.Person.Email, [alpha.Person.Email]);

        await _giftExchangeProvider.UpdateEligibleRecipientsAsync(
            hat.OrganizerEmail, hat.HatId, charlie.Person.Email, [beta.Person.Email]);

        var participants = await _giftExchangeProvider.GetParticipantsAsync(hat.OrganizerEmail, hat.HatId);

        return new SourceHat(hat, participants);
    }

    /// <summary>Everybody drawing somebody, which is what a reset has to clear.</summary>
    private async Task ShakeAsync(SourceHat source)
    {
        var participants = source.Participants;

        for (var index = 0; index < participants.Count; index++)
            await _giftExchangeProvider.UpdateParticipantPickedRecipientAsync(
                source.Hat.OrganizerEmail,
                source.Hat.HatId,
                participants[index].Person.Email,
                participants[(index + 1) % participants.Count].Person.Email);

        await _giftExchangeProvider.UpdateHatStatusAsync(
            source.Hat.OrganizerEmail, source.Hat.HatId, HatStatus.NamesAssigned);
    }

    private async Task<Participant> AddParticipantAsync(
        HatDataModel hat,
        ImmutableList<Participant> existingParticipants
    ) =>
        await _giftExchangeProvider.CreateParticipantAsync(
            _addParticipantRequestFaker.Generate() with { HatId = hat.HatId, OrganizerEmail = hat.OrganizerEmail },
            existingParticipants);

    private async Task<HttpStatusCode> ResetAsync(SourceHat source)
    {
        var body = _jsonService.SerializeDefault(new ResetHatRequest
        {
            OrganizerEmail = source.Hat.OrganizerEmail,
            HatId = source.Hat.HatId
        });

        var response = await _sut.FunctionHandler(body.ToApiGatewayProxyRequest(), _context);

        return (HttpStatusCode)response.StatusCode;
    }

    private async Task<Hat> GetAsync(SourceHat source)
    {
        var (exists, hat) = await _giftExchangeProvider.GetHatAsync(source.Hat.OrganizerEmail, source.Hat.HatId);

        exists.Should().BeTrue();

        return hat;
    }

    private static Dictionary<string, List<string>> EligibilityIn(Hat hat) =>
        EligibilityIn(hat.Participants);

    private static Dictionary<string, List<string>> EligibilityIn(IEnumerable<Participant> participants) =>
        participants.ToDictionary(
            participant => participant.Person.Email,
            participant => participant.EligibleRecipients.Select(recipient => recipient.Email).OrderBy(email => email).ToList());

    private sealed record SourceHat(HatDataModel Hat, ImmutableList<Participant> Participants);
}
