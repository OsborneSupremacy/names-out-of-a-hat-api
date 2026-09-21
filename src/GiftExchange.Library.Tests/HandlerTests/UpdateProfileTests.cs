namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class UpdateProfileTests
{
    private readonly ILambdaContext _context;

    private readonly JsonService _jsonService;

    private readonly GiftExchangeProvider _provider;

    private readonly IApiGatewayHandler _sut;

    public UpdateProfileTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        var contextFactory = dbFixture.CreateContextFactory();
        _context = new FakeLambdaContext();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddValidators()
            .AddSingleton(contextFactory)
            .AddSingleton<IContentModerationService, FakeContentModerationService>()
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("put/profile");
    }

    [Fact]
    public async Task UpdateProfile_RenamesTheOrganizerOnTheirHatAndTheirOwnParticipantRow()
    {
        // arrange
        var hat = await CreateHatWithOrganizerAsync();

        // act
        var response = await UpdateNameAsync(hat.OrganizerEmail, "Renamed Person");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var (_, stored) = await _provider.GetHatAsync(hat.OrganizerEmail, hat.HatId);
        stored.Organizer.Name.Should().Be("Renamed Person");
        stored.Participants
            .Single(participant => participant.Person.Email == hat.OrganizerEmail)
            .Person.Name.Should().Be("Renamed Person");
    }

    [Fact]
    public async Task UpdateProfile_GivenANameAnotherParticipantAlreadyUses_ReturnsConflict()
    {
        // arrange
        var hat = await CreateHatWithOrganizerAsync();

        await _provider.CreateParticipantAsync(new AddParticipantRequest
        {
            OrganizerEmail = hat.OrganizerEmail,
            HatId = hat.HatId,
            Name = "Taken Name",
            Email = "someone.else@example.com"
        }, []);

        // act: compared case-insensitively, the same way AddParticipantService compares names.
        var response = await UpdateNameAsync(hat.OrganizerEmail, "taken name");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);

        var (_, stored) = await _provider.GetHatAsync(hat.OrganizerEmail, hat.HatId);
        stored.Organizer.Name.Should().Be(hat.OrganizerName);
    }

    /// <summary>
    /// A name belongs to the person, not to a membership, so changing it is felt everywhere they
    /// appear — including exchanges somebody else organizes.
    ///
    /// This used to assert the opposite: the rename was applied hat by hat, and deliberately
    /// skipped hats the renamer did not own, because a name was stored once per hat and the copies
    /// could disagree. There are no copies now. Bob is one row, and there is no version of him that
    /// could stay behind in Alice's exchange.
    /// </summary>
    [Fact]
    public async Task UpdateProfile_RenamesThemEverywhereTheyAppear()
    {
        // arrange: Bob is a participant in Alice's exchange, and organizes one of his own.
        var bobEmail = FakeValues.Email(new Bogus.Faker());

        var alicesHat = await CreateHatWithOrganizerAsync();

        await _provider.CreateParticipantAsync(new AddParticipantRequest
        {
            OrganizerEmail = alicesHat.OrganizerEmail,
            HatId = alicesHat.HatId,
            Name = "Bob Original",
            Email = bobEmail
        }, []);

        var bobsHat = await CreateHatWithOrganizerAsync(bobEmail);

        // act
        var response = await UpdateNameAsync(bobEmail, "Bob Renamed");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var (_, bobs) = await _provider.GetHatAsync(bobEmail, bobsHat.HatId);
        bobs.Organizer.Name.Should().Be("Bob Renamed");

        var (_, alices) = await _provider.GetHatAsync(alicesHat.OrganizerEmail, alicesHat.HatId);
        alices.Participants
            .Single(participant => participant.Person.Email == bobEmail)
            .Person.Name.Should().Be("Bob Renamed");

        // Alice is a different person, so nothing about her moved.
        alices.Organizer.Name.Should().Be(alicesHat.OrganizerName);
    }

    /// <summary>
    /// The gap that converging on <c>RenamePersonAsync</c> closed. This check used to look only at
    /// exchanges the renamer organizes, and a rename reaches every exchange they are in — so
    /// somebody could rename themselves into a collision in an exchange run by anybody else.
    ///
    /// The refusal does not name that exchange. Whose it is, and who else is in it, is not the
    /// renamer's to learn from a message about their own profile.
    /// </summary>
    [Fact]
    public async Task UpdateProfile_GivenANameTakenInSomebodyElsesExchange_ReturnsConflict()
    {
        // arrange: Bob takes part in Alice's exchange, which already has a Dave in it.
        var bobEmail = FakeValues.Email(new Bogus.Faker());

        var alicesHat = await CreateHatWithOrganizerAsync();

        await _provider.CreateParticipantAsync(new AddParticipantRequest
        {
            OrganizerEmail = alicesHat.OrganizerEmail,
            HatId = alicesHat.HatId,
            Name = "Bob Original",
            Email = bobEmail
        }, []);

        await _provider.CreateParticipantAsync(new AddParticipantRequest
        {
            OrganizerEmail = alicesHat.OrganizerEmail,
            HatId = alicesHat.HatId,
            Name = "Dave",
            Email = "dave.in.alices.exchange@example.com"
        }, []);

        // act: Bob renames himself, in a session of his own rather than in Alice's exchange.
        var response = await UpdateNameAsync(bobEmail, "Dave");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        response.Body.Should().NotContain(alicesHat.HatName);
    }

    /// <summary>
    /// Somebody who has signed in but created nothing has no person row yet, so setting a name is
    /// how they get one. It used to be a silent no-op that answered OK and wrote nothing.
    /// </summary>
    [Fact]
    public async Task UpdateProfile_ForSomebodyWithNoRecordYet_WritesOne()
    {
        // arrange
        var email = $"newcomer.{Guid.CreateVersion7():N}@example.com";

        // act
        var response = await UpdateNameAsync(email, "Newly Named");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var page = await _provider.GetHatsAsync(new GetHatsPageRequest { OrganizerEmail = email, Page = 1, PageSize = 1 });
        page.OrganizerName.Should().Be("Newly Named");
    }

    private Task<APIGatewayProxyResponse> UpdateNameAsync(string organizerEmail, string name) =>
        _sut.FunctionHandler(
            _jsonService
                .SerializeDefault(new UpdateProfileRequest { Name = name })
                .ToApiGatewayProxyRequest(organizerEmail),
            _context);

    /// <summary>
    /// Mirrors CreateHatService: the organizer is a participant in their own exchange. Both rows
    /// point at the same person, so the name they are shown by is stored once.
    /// </summary>
    private async Task<HatDataModel> CreateHatWithOrganizerAsync(string? organizerEmail = null)
    {
        var hat = new HatDataModelFaker().Generate();

        if (organizerEmail is not null)
            hat = hat with { OrganizerEmail = organizerEmail };

        await _provider.CreateHatAsync(hat);

        await _provider.CreateParticipantAsync(new AddParticipantRequest
        {
            OrganizerEmail = hat.OrganizerEmail,
            HatId = hat.HatId,
            Name = hat.OrganizerName,
            Email = hat.OrganizerEmail
        }, []);

        return hat;
    }
}
