namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class CopyHatTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly HatDataModelFaker _hatDataModelFaker;

    private readonly AddParticipantRequestFaker _addParticipantRequestFaker;

    private readonly IApiGatewayHandler _sut;

    public CopyHatTests(PostgresFixture dbFixture)
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
        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("post/hat/copy");
    }

    [Fact]
    public async Task CopyHat_GivenARevealedHat_CopiesItsDetailsAndParticipants()
    {
        // arrange
        var source = await CreateRevealedHatAsync();

        // act
        var (statusCode, copy) = await CopyAsync(source, excludePreviousRecipients: false);

        // assert
        statusCode.Should().Be(HttpStatusCode.Created);

        copy.Id.Should().NotBe(source.Hat.HatId);
        copy.Status.Should().Be(HatStatus.InProgress);
        copy.AdditionalInformation.Should().Be(source.Hat.AdditionalInformation);
        copy.PriceRange.Should().Be(source.Hat.PriceRange);
        copy.Organizer.Should().BeEquivalentTo(new Person
        {
            Name = source.Hat.OrganizerName,
            Email = source.Hat.OrganizerEmail
        });

        copy.Participants
            .Select(participant => participant.Person)
            .Should()
            .BeEquivalentTo(source.Participants.Select(participant => participant.Person));
    }

    [Fact]
    public async Task CopyHat_GivenARevealedHat_LeavesEveryoneWithoutAPick()
    {
        // arrange
        var source = await CreateRevealedHatAsync();

        // act
        var (_, copy) = await CopyAsync(source, excludePreviousRecipients: false);

        // assert
        copy.Participants.Should().OnlyContain(participant => participant.PickedRecipient == string.Empty);
    }

    [Fact]
    public async Task CopyHat_WithoutExcludingPreviousRecipients_KeepsTheEligibilityRulesAsTheyWere()
    {
        // arrange: Alpha may draw anyone, Beta may not draw Charlie.
        var source = await CreateRevealedHatAsync();

        // act
        var (_, copy) = await CopyAsync(source, excludePreviousRecipients: false);

        // assert
        EligibilityIn(copy).Should().BeEquivalentTo(EligibilityIn(await GetSourceAsync(source)));
    }

    [Fact]
    public async Task CopyHat_ExcludingPreviousRecipients_DropsWhoeverEachParticipantDrew()
    {
        // arrange
        var source = await CreateRevealedHatAsync();

        // act
        var (_, copy) = await CopyAsync(source, excludePreviousRecipients: true);

        // assert
        foreach (var participant in source.Participants)
        {
            var copied = copy.Participants.Single(other => other.Person.Email == participant.Person.Email);

            copied.EligibleRecipients.Should().NotContain(participant.PickedRecipient);
            copied.EligibleRecipients.Should().BeEquivalentTo(
                participant.EligibleRecipients.Where(name => name != participant.PickedRecipient));
        }
    }

    [Fact]
    public async Task CopyHat_ExcludingPreviousRecipients_LeavesTheSourceHatAlone()
    {
        // arrange
        var source = await CreateRevealedHatAsync();

        // act
        _ = await CopyAsync(source, excludePreviousRecipients: true);

        // assert
        var reread = await GetSourceAsync(source);

        reread.Status.Should().Be(HatStatus.Closed);
        EligibilityIn(reread).Should().BeEquivalentTo(EligibilityIn(source.Participants));
        reread.Participants.Should().OnlyContain(participant => participant.PickedRecipient != string.Empty);
    }

    [Fact]
    public async Task CopyHat_GivenAHatThatIsNotRevealed_ReturnsConflictResponse()
    {
        // arrange
        var source = await CreateRevealedHatAsync();

        await _giftExchangeProvider
            .UpdateHatStatusAsync(source.Hat.OrganizerEmail, source.Hat.HatId, HatStatus.CooledOff);

        // act
        var (statusCode, _) = await CopyAsync(source, excludePreviousRecipients: true, expectSuccess: false);

        // assert
        statusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CopyHat_GivenANameTheOrganizerAlreadyUses_ReturnsConflictResponse()
    {
        // arrange
        var source = await CreateRevealedHatAsync();

        // act: the source's own name is, by definition, already taken.
        var (statusCode, _) = await CopyAsync(
            source,
            excludePreviousRecipients: true,
            newHatName: source.Hat.HatName,
            expectSuccess: false);

        // assert
        statusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CopyHat_GivenSomebodyElsesHat_ReturnsNotFoundResponse()
    {
        // arrange
        var source = await CreateRevealedHatAsync();

        var body = _jsonService.SerializeDefault(new CopyHatRequest
        {
            OrganizerEmail = source.Hat.OrganizerEmail,
            HatId = source.Hat.HatId,
            NewHatName = _hatDataModelFaker.Generate().HatName,
            ExcludePreviousRecipients = true
        });

        // act: the body names the owner, but the authenticated caller is somebody else.
        var response = await _sut.FunctionHandler(
            body.ToApiGatewayProxyRequest("intruder@example.com"),
            _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CopyHat_LeavesOutAnybodyWhoHasRefused()
    {
        // arrange: this is the one path that adds a whole exchange's worth of people at once, and
        // so the one place a refusal made last year is silently reversed a year later.
        var source = await CreateRevealedHatAsync();
        var refuser = source.Participants[1];

        await _giftExchangeProvider.RecordDoNotAddAsync(new RecordDoNotAddRequest
        {
            Email = refuser.Person.Email,
            HatId = source.Hat.HatId,
            OrganizerEmail = source.Hat.OrganizerEmail,
            BlockOrganizer = false,
            BlockAnywhere = false
        });

        // act
        var (statusCode, copy) = await CopyAsync(source, excludePreviousRecipients: false);

        // assert: the copy succeeds and is simply smaller. Refusing it outright would leave the
        // organizer to work out by elimination which of their friends had opted out.
        statusCode.Should().Be(HttpStatusCode.Created);

        copy.Participants.Select(participant => participant.Person.Email)
            .Should().NotContain(refuser.Person.Email);

        copy.Participants.Should().HaveCount(source.Participants.Count - 1);
    }

    [Fact]
    public async Task CopyHat_SaysHowManyWereLeftOutWithoutSayingWho()
    {
        // arrange
        var source = await CreateRevealedHatAsync();
        var refuser = source.Participants[2];

        await _giftExchangeProvider.RecordDoNotAddAsync(new RecordDoNotAddRequest
        {
            Email = refuser.Person.Email,
            HatId = source.Hat.HatId,
            OrganizerEmail = source.Hat.OrganizerEmail,
            BlockOrganizer = false,
            BlockAnywhere = false
        });

        var request = new CopyHatRequest
        {
            OrganizerEmail = source.Hat.OrganizerEmail,
            HatId = source.Hat.HatId,
            NewHatName = _hatDataModelFaker.Generate().HatName,
            ExcludePreviousRecipients = false
        };

        // act
        var response = await _sut.FunctionHandler(
            _jsonService.SerializeDefault(request).ToApiGatewayProxyRequest(),
            _context);

        // assert: a count and not a list. The organizer needs to know the copy is smaller than what
        // it was copied from, or they will send invitations to a short exchange without noticing;
        // naming them would undo the refusal by another route.
        var body = _jsonService.DeserializeDefault<CopyHatResponse>(response.Body);

        body!.ParticipantsOmitted.Should().Be(1);
        response.Body.Should().NotContain(refuser.Person.Email);
        response.Body.Should().NotContain(refuser.Person.Name);
    }

    [Fact]
    public async Task CopyHat_WithNobodyRefusing_ReportsNoneOmitted()
    {
        // arrange: the ordinary copy, which is nearly all of them.
        var source = await CreateRevealedHatAsync();

        var request = new CopyHatRequest
        {
            OrganizerEmail = source.Hat.OrganizerEmail,
            HatId = source.Hat.HatId,
            NewHatName = _hatDataModelFaker.Generate().HatName,
            ExcludePreviousRecipients = false
        };

        // act
        var response = await _sut.FunctionHandler(
            _jsonService.SerializeDefault(request).ToApiGatewayProxyRequest(),
            _context);

        // assert
        _jsonService.DeserializeDefault<CopyHatResponse>(response.Body)!
            .ParticipantsOmitted.Should().Be(0);
    }

    /// <summary>
    /// A copy is another gift exchange, so it spends the same allowance one created from scratch
    /// does — including when the organizer spent the rest of it elsewhere.
    /// </summary>
    [Fact]
    public async Task CopyHat_GivenTheOrganizerHasSpentTheirDailyLimit_TooManyRequests()
    {
        // arrange
        var source = await CreateRevealedHatAsync();

        // The source hat is the first of the day; these bring the organizer up to the limit.
        for (var created = 1; created < HatCreationLimiter.DailyLimit; created++)
            await _giftExchangeProvider.CreateHatAsync(_hatDataModelFaker.Generate() with
            {
                OrganizerName = source.Hat.OrganizerName,
                OrganizerEmail = source.Hat.OrganizerEmail
            });

        // act
        var (statusCode, _) = await CopyAsync(source, excludePreviousRecipients: false, expectSuccess: false);

        // assert
        statusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// A copy is another open exchange too. The source is closed, so it is only the others that
    /// count against the open limit. They spend the daily allowance as well, and the 409 is what
    /// shows the open limit being answered first, which is the one waiting would not fix.
    /// </summary>
    [Fact]
    public async Task CopyHat_GivenTheOrganizerHasReachedTheOpenLimit_Conflict()
    {
        // arrange
        var source = await CreateRevealedHatAsync();

        for (var created = 0; created < HatCreationLimiter.OpenLimit; created++)
            await _giftExchangeProvider.CreateHatAsync(_hatDataModelFaker.Generate() with
            {
                OrganizerName = source.Hat.OrganizerName,
                OrganizerEmail = source.Hat.OrganizerEmail
            });

        // act
        var (statusCode, _) = await CopyAsync(source, excludePreviousRecipients: false, expectSuccess: false);

        // assert
        statusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// An exchange created before the participant limit existed can be larger than a new one is
    /// allowed to be. Copying it is refused with something an organizer can act on, rather than
    /// left to arrive as a transaction too large to commit.
    /// </summary>
    [Fact]
    public async Task CopyHat_GivenTheSourceHoldsMoreThanTheLimit_ConflictResponse()
    {
        // arrange
        var source = await CreateRevealedHatAsync();

        var participants = source.Participants;

        while (participants.Count <= ParticipantLimit.MaxParticipants)
            participants = participants.Add(await AddParticipantAsync(source.Hat, participants));

        // act
        var (statusCode, _) = await CopyAsync(source, excludePreviousRecipients: false, expectSuccess: false);

        // assert
        statusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A revealed hat with three participants, one eligibility rule of its own, and everybody
    /// holding a pick — enough for the copy to have something to carry over and something to drop.
    /// </summary>
    private async Task<SourceHat> CreateRevealedHatAsync()
    {
        var hat = _hatDataModelFaker.Generate();
        await _giftExchangeProvider.CreateHatAsync(hat);

        var alpha = await AddParticipantAsync(hat, []);
        var beta = await AddParticipantAsync(hat, [alpha]);
        var charlie = await AddParticipantAsync(hat, [alpha, beta]);

        // Beta is not allowed to draw Charlie — the kind of standing rule a copy exists to keep.
        await _giftExchangeProvider.UpdateEligibleRecipientsAsync(
            hat.OrganizerEmail, hat.HatId, beta.Person.Email, [alpha.Person.Name]);

        await PickAsync(hat, alpha, beta);
        await PickAsync(hat, beta, alpha);
        await PickAsync(hat, charlie, alpha);

        await _giftExchangeProvider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.Closed);

        var participants = await _giftExchangeProvider.GetParticipantsAsync(hat.OrganizerEmail, hat.HatId);

        return new SourceHat(hat, participants);
    }

    private async Task<Participant> AddParticipantAsync(
        HatDataModel hat,
        ImmutableList<Participant> existingParticipants
    ) =>
        await _giftExchangeProvider.CreateParticipantAsync(
            _addParticipantRequestFaker.Generate() with { HatId = hat.HatId, OrganizerEmail = hat.OrganizerEmail },
            existingParticipants);

    private Task PickAsync(HatDataModel hat, Participant giver, Participant recipient) =>
        _giftExchangeProvider.UpdateParticipantPickedRecipientAsync(
            hat.OrganizerEmail, hat.HatId, giver.Person.Email, recipient.Person.Name);

    private async Task<(HttpStatusCode statusCode, Hat copy)> CopyAsync(
        SourceHat source,
        bool excludePreviousRecipients,
        string? newHatName = null,
        bool expectSuccess = true
    )
    {
        var request = new CopyHatRequest
        {
            OrganizerEmail = source.Hat.OrganizerEmail,
            HatId = source.Hat.HatId,
            NewHatName = newHatName ?? _hatDataModelFaker.Generate().HatName,
            ExcludePreviousRecipients = excludePreviousRecipients
        };

        var response = await _sut.FunctionHandler(
            _jsonService.SerializeDefault(request).ToApiGatewayProxyRequest(),
            _context);

        var statusCode = (HttpStatusCode)response.StatusCode;

        if (!expectSuccess)
            return (statusCode, Hats.Empty);

        var body = _jsonService.DeserializeDefault<CopyHatResponse>(response.Body);
        body.Should().NotBeNull();

        var (exists, copy) = await _giftExchangeProvider
            .GetHatAsync(source.Hat.OrganizerEmail, body!.HatId);

        exists.Should().BeTrue();

        return (statusCode, copy);
    }

    private async Task<Hat> GetSourceAsync(SourceHat source)
    {
        var (_, hat) = await _giftExchangeProvider.GetHatAsync(source.Hat.OrganizerEmail, source.Hat.HatId);
        return hat;
    }

    /// <summary>Eligibility keyed by email, so it compares across two hats whose ids differ.</summary>
    private static Dictionary<string, List<string>> EligibilityIn(Hat hat) =>
        EligibilityIn(hat.Participants);

    private static Dictionary<string, List<string>> EligibilityIn(IEnumerable<Participant> participants) =>
        participants.ToDictionary(
            participant => participant.Person.Email,
            participant => participant.EligibleRecipients.OrderBy(name => name).ToList());

    private sealed record SourceHat(HatDataModel Hat, ImmutableList<Participant> Participants);
}
