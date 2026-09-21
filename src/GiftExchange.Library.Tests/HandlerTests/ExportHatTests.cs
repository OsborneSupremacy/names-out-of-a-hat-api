namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class ExportHatTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly HatDataModelFaker _hatDataModelFaker;

    private readonly AddParticipantRequestFaker _addParticipantRequestFaker;

    private readonly IApiGatewayHandler _sut;

    public ExportHatTests(PostgresFixture dbFixture)
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
        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("get/hat/{email}/export/{id}");
    }

    [Fact]
    public async Task ExportHat_GivenAnExchange_DescribesItAndEverybodyInIt()
    {
        // arrange
        var source = await CreateDrawnHatAsync();

        // act
        var export = await ExportAsync(source);

        // assert
        export.FormatVersion.Should().Be("1");
        export.ExportedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        export.Hat.HatId.Should().Be(source.Hat.HatId);
        export.Hat.Name.Should().Be(source.Hat.HatName);
        export.Hat.AdditionalInformation.Should().Be(source.Hat.AdditionalInformation);
        export.Hat.PriceRange.Should().Be(source.Hat.PriceRange);
        export.Hat.Organizer.Email.Should().Be(source.Hat.OrganizerEmail);

        export.Hat.Participants
            .Select(participant => participant.Person.Email)
            .Should()
            .BeEquivalentTo(source.Participants.Select(participant => participant.Person.Email));
    }

    /// <summary>
    /// The ids are the reason this is an export rather than a screenshot: they are what lets the
    /// document say who drew whom without leaning on display names.
    /// </summary>
    [Fact]
    public async Task ExportHat_CarriesTheIdentifiersBehindEveryName()
    {
        // arrange
        var source = await CreateDrawnHatAsync();

        // act
        var export = await ExportAsync(source);

        // assert
        export.Hat.Organizer.PersonId.Should().NotBe(Guid.Empty);
        export.Hat.Participants.Should().OnlyContain(participant => participant.ParticipantId != Guid.Empty);
        export.Hat.Participants.Should().OnlyContain(participant => participant.Person.PersonId != Guid.Empty);

        var participantIds = export.Hat.Participants
            .Select(participant => participant.ParticipantId)
            .ToList();

        // Every eligibility reference resolves to somebody in the same document.
        export.Hat.Participants
            .SelectMany(participant => participant.EligibleRecipients)
            .Should()
            .OnlyContain(reference => participantIds.Contains(reference.ParticipantId));
    }

    [Fact]
    public async Task ExportHat_CarriesWhoEachParticipantMayDraw()
    {
        // arrange: Beta may draw only Alpha, which is the standing rule this export exists to keep.
        var source = await CreateDrawnHatAsync();
        var beta = source.Participants[1];

        // act
        var export = await ExportAsync(source);

        // assert
        var exported = export.Hat.Participants.Single(participant => participant.Person.Email == beta.Person.Email);

        exported.EligibleRecipients
            .Select(reference => reference.Name)
            .Should()
            .BeEquivalentTo(beta.EligibleRecipients.Select(recipient => recipient.Name));
    }

    /// <summary>
    /// The exchange keeps the draw from its own organizer until they reveal it, and an export is
    /// another way of asking the same question. A second way of asking, answered differently, would
    /// be the way around the first one.
    /// </summary>
    [Theory]
    [InlineData("NAMES_ASSIGNED")]
    [InlineData("INVITATIONS_SENT")]
    [InlineData("READY_TO_CLOSE")]
    public async Task ExportHat_BeforeThePicksAreRevealed_LeavesThemOut(string status)
    {
        // arrange
        var source = await CreateDrawnHatAsync();
        await _giftExchangeProvider.UpdateHatStatusAsync(source.Hat.OrganizerEmail, source.Hat.HatId, status);

        // act
        var response = await GetResponseAsync(source);
        var export = _jsonService.DeserializeDefault<ExportHatResponse>(response.Body)!;

        // assert
        export.Hat.Participants.Should().OnlyContain(participant =>
            participant.PickedRecipient.ParticipantId == Guid.Empty
            && participant.PickedRecipient.Name == string.Empty);

        // Not merely absent from the deserialized record: absent from the document. Whoever each
        // participant drew is in the hat, so a name leaking anywhere else in the body would still
        // be a leak -- but every one of them appears legitimately as a participant, so the check
        // that means anything is that no pick points at one.
        response.Body.Should().NotContain(Persons.Redacted.Name);
    }

    [Fact]
    public async Task ExportHat_OnceThePicksAreRevealed_SaysWhoDrewWhom()
    {
        // arrange
        var source = await CreateDrawnHatAsync();
        await _giftExchangeProvider.UpdateHatStatusAsync(source.Hat.OrganizerEmail, source.Hat.HatId, HatStatus.Closed);

        // act
        var export = await ExportAsync(source);

        // assert
        var byEmail = export.Hat.Participants.ToDictionary(participant => participant.Person.Email);

        foreach (var participant in source.Participants)
            byEmail[participant.Person.Email].PickedRecipient.Name.Should().Be(participant.PickedRecipient.Name);

        // And the reference resolves, rather than only reading correctly.
        var alpha = byEmail[source.Participants[0].Person.Email];
        var drawn = export.Hat.Participants.Single(participant => participant.ParticipantId == alpha.PickedRecipient.ParticipantId);

        drawn.Person.Email.Should().Be(source.Participants[0].PickedRecipient.Email);
    }

    /// <summary>
    /// Nobody has drawn anybody yet, so there is no pick to withhold and none to disclose.
    /// </summary>
    [Fact]
    public async Task ExportHat_BeforeTheHatIsShaken_LeavesEveryPickEmpty()
    {
        // arrange
        var hat = _hatDataModelFaker.Generate();
        await _giftExchangeProvider.CreateHatAsync(hat);

        var alpha = await AddParticipantAsync(hat, []);
        await AddParticipantAsync(hat, [alpha]);

        var participants = await _giftExchangeProvider.GetParticipantsAsync(hat.OrganizerEmail, hat.HatId);

        // act
        var export = await ExportAsync(new SourceHat(hat, participants));

        // assert
        export.Hat.Status.Should().Be(HatStatus.InProgress);
        export.Hat.Participants.Should().OnlyContain(participant =>
            participant.PickedRecipient.ParticipantId == Guid.Empty);
    }

    /// <summary>
    /// The organizer's own address, recorded when they send. It is a fact about them rather than
    /// about the exchange, and this is a file made to be moved around.
    /// </summary>
    [Fact]
    public async Task ExportHat_DoesNotCarryTheAddressInvitationsWereSentFrom()
    {
        // arrange
        var source = await CreateDrawnHatAsync();

        await _giftExchangeProvider.MarkInvitationsAsQueuedAsync(
            source.Hat.OrganizerEmail, source.Hat.HatId, "198.51.100.7");

        // act
        var response = await GetResponseAsync(source);

        // assert
        response.Body.Should().NotContain("198.51.100.7");
        response.Body.Should().NotContain("invitationsSentFromIp");
    }

    [Fact]
    public async Task ExportHat_GivenSomebodyElsesHat_ReturnsNotFoundResponse()
    {
        // arrange
        var source = await CreateDrawnHatAsync();

        // act: the path names the exchange, but the authenticated caller is somebody else.
        var response = await _sut.FunctionHandler(
            RequestFor(source.Hat.HatId, "intruder@example.com"),
            _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportHat_GivenAHatThatDoesNotExist_ReturnsNotFoundResponse()
    {
        // arrange
        var source = await CreateDrawnHatAsync();

        // act
        var response = await _sut.FunctionHandler(
            RequestFor(Guid.NewGuid(), source.Hat.OrganizerEmail),
            _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Three participants, one eligibility rule of the organizer's own, and everybody holding a
    /// pick — enough for the export to have something to withhold and something to resolve.
    /// </summary>
    private async Task<SourceHat> CreateDrawnHatAsync()
    {
        var hat = _hatDataModelFaker.Generate();
        await _giftExchangeProvider.CreateHatAsync(hat);

        var alpha = await AddParticipantAsync(hat, []);
        var beta = await AddParticipantAsync(hat, [alpha]);
        var charlie = await AddParticipantAsync(hat, [alpha, beta]);

        await _giftExchangeProvider.UpdateEligibleRecipientsAsync(
            hat.OrganizerEmail, hat.HatId, beta.Person.Email, [alpha.Person.Email]);

        await PickAsync(hat, alpha, beta);
        await PickAsync(hat, beta, alpha);
        await PickAsync(hat, charlie, alpha);

        await _giftExchangeProvider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.NamesAssigned);

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
            hat.OrganizerEmail, hat.HatId, giver.Person.Email, recipient.Person.Email);

    private static APIGatewayProxyRequest RequestFor(Guid hatId, string authenticatedEmail) =>
        new APIGatewayProxyRequest
        {
            PathParameters = new Dictionary<string, string> { { "id", hatId.ToString() } }
        }.WithAuthenticatedEmail(authenticatedEmail);

    private Task<APIGatewayProxyResponse> GetResponseAsync(SourceHat source) =>
        _sut.FunctionHandler(RequestFor(source.Hat.HatId, source.Hat.OrganizerEmail), _context);

    private async Task<ExportHatResponse> ExportAsync(SourceHat source)
    {
        var response = await GetResponseAsync(source);

        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var body = _jsonService.DeserializeDefault<ExportHatResponse>(response.Body);
        body.Should().NotBeNull();

        return body!;
    }

    private sealed record SourceHat(HatDataModel Hat, ImmutableList<Participant> Participants);
}
