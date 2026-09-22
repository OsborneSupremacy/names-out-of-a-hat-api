using GiftExchange.Library.Utility;

namespace GiftExchange.Library.Tests.HandlerTests;

/// <summary>
/// Renaming a participant, against a real database.
///
/// Two things are being pinned down. The first is that nothing about the draw moves: eligibility
/// and picks are participant ids, so a rename after the hat is shaken has to leave the hat shaken
/// and the pick intact — which is precisely what removing and re-adding somebody, the only remedy
/// an organizer had before, does not do.
///
/// The second is reach. A name is one row on one person, so renaming somebody is felt in every
/// exchange they take part in. That is deliberate, and it is also what makes the collision check
/// wider than it looks: a name free in this hat can be taken in one this organizer cannot see.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EditParticipantNameTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly GiftExchangeProvider _provider;

    private readonly TestDataService _testDataService;

    private readonly IApiGatewayHandler _sut;

    public EditParticipantNameTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        var contextFactory = dbFixture.CreateContextFactory();
        _context = new FakeLambdaContext();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .AddSingleton<IContentModerationService, FakeContentModerationService>()
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _testDataService = new TestDataService(_provider);

        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("put/participant/name");
    }

    [Fact]
    public async Task EditName_RenamesTheParticipant()
    {
        // arrange
        var hat = await CreateHatWithOrganizerAsync();
        var participant = await AddParticipantAsync(hat, "Original Name");

        // act
        var response = await RenameAsync(hat.Organizer.Email, hat.Id, participant.Email, "Corrected Name");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var stored = await _testDataService.GetParticipantAsync(hat.Organizer.Email, hat.Id, participant.Email);
        stored.Person.Name.Should().Be("Corrected Name");
    }

    /// <summary>
    /// The reason this endpoint exists apart from PUT /participant, which resets the hat to
    /// IN_PROGRESS as it edits eligibility. A rename touches nothing the draw is made of, so a
    /// shaken hat has to stay shaken and the pick has to survive.
    /// </summary>
    [Fact]
    public async Task EditName_AfterTheHatIsShaken_LeavesTheDrawAlone()
    {
        // arrange
        var hat = await CreateHatWithOrganizerAsync();
        var giver = await AddParticipantAsync(hat, "Giver");
        var receiver = await AddParticipantAsync(hat, "Receiver");

        await _provider.UpdateParticipantPickedRecipientAsync(
            hat.Organizer.Email, hat.Id, giver.Email, receiver.Email);

        await _provider.UpdateHatStatusAsync(hat.Organizer.Email, hat.Id, HatStatus.NamesAssigned);

        // act
        var response = await RenameAsync(hat.Organizer.Email, hat.Id, receiver.Email, "Receiver Renamed");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var stored = await _testDataService.GetHatAsync(hat.Organizer.Email, hat.Id);

        stored.Status.Should().Be(HatStatus.NamesAssigned);

        // The pick is stored as an id and projected back through the person row, so it follows the
        // rename rather than pointing at a name nobody answers to any more.
        stored.Participants
            .Single(candidate => candidate.Person.Email == giver.Email)
            .PickedRecipient.Name.Should().Be("Receiver Renamed");
    }

    /// <summary>
    /// A name belongs to the person rather than to one membership, so it moves everywhere at once
    /// — the same property <c>UpdateProfileService</c> has when somebody renames themselves.
    /// </summary>
    [Fact]
    public async Task EditName_RenamesThemInEveryExchangeTheyAreIn()
    {
        // arrange: Bob is in Alice's exchange and in Carol's.
        var alicesHat = await CreateHatWithOrganizerAsync();
        var carolsHat = await CreateHatWithOrganizerAsync();

        var bob = await AddParticipantAsync(alicesHat, "Bob Original");
        await AddParticipantAsync(carolsHat, "Bob Original", bob.Email);

        // act: Alice renames him in hers.
        var response = await RenameAsync(alicesHat.Organizer.Email, alicesHat.Id, bob.Email, "Bob Renamed");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var inCarols = await _testDataService
            .GetParticipantAsync(carolsHat.Organizer.Email, carolsHat.Id, bob.Email);

        inCarols.Person.Name.Should().Be("Bob Renamed");
    }

    /// <summary>
    /// Two people in one exchange may share a name. What an exchange cannot hold is the same person
    /// twice, and the address is what says who a person is.
    /// </summary>
    [Fact]
    public async Task EditName_GivenANameAnotherParticipantHereUses_IsAccepted()
    {
        // arrange
        var hat = await CreateHatWithOrganizerAsync("The Colliding Exchange");
        var participant = await AddParticipantAsync(hat, "Original Name");
        await AddParticipantAsync(hat, "Taken Name");

        // act
        var response = await RenameAsync(hat.Organizer.Email, hat.Id, participant.Email, "Taken Name");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var stored = await _testDataService.GetParticipantAsync(hat.Organizer.Email, hat.Id, participant.Email);
        stored.Person.Name.Should().Be("Taken Name");
    }

    /// <summary>
    /// A rename reaches every exchange the person is in, and used to be refused over a name taken
    /// in an exchange the caller does not even run. Names are not unique within an exchange any
    /// more, so there is nothing there to refuse.
    /// </summary>
    [Fact]
    public async Task EditName_GivenANameTakenInSomebodyElsesExchange_IsAccepted()
    {
        // arrange: Bob is in Alice's exchange and in Carol's, and Carol's already has a Dave.
        var alicesHat = await CreateHatWithOrganizerAsync("Alices Exchange");
        var carolsHat = await CreateHatWithOrganizerAsync("Carols Exchange");

        var bob = await AddParticipantAsync(alicesHat, "Bob Original");
        await AddParticipantAsync(carolsHat, "Bob Original", bob.Email);
        await AddParticipantAsync(carolsHat, "Dave");

        // act
        var response = await RenameAsync(alicesHat.Organizer.Email, alicesHat.Id, bob.Email, "Dave");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var inCarols = await _testDataService
            .GetParticipantAsync(carolsHat.Organizer.Email, carolsHat.Id, bob.Email);

        inCarols.Person.Name.Should().Be("Dave");
    }

    /// <summary>
    /// Fixing the capitalisation of a name is an ordinary rename.
    /// </summary>
    [Fact]
    public async Task EditName_ChangingOnlyTheCapitalisation_IsAccepted()
    {
        // arrange
        var hat = await CreateHatWithOrganizerAsync();
        var participant = await AddParticipantAsync(hat, "bob mcbobface");

        // act
        var response = await RenameAsync(hat.Organizer.Email, hat.Id, participant.Email, "Bob McBobface");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var stored = await _testDataService.GetParticipantAsync(hat.Organizer.Email, hat.Id, participant.Email);
        stored.Person.Name.Should().Be("Bob McBobface");
    }

    /// <summary>
    /// The organizer is a participant of their own exchange, and both rows point at the same
    /// person, so renaming themselves through this endpoint moves the name the hat is organized
    /// under too.
    /// </summary>
    [Fact]
    public async Task EditName_AppliedToTheOrganizer_MovesTheNameTheHatIsOrganizedUnder()
    {
        // arrange
        var hat = await CreateHatWithOrganizerAsync();

        // act
        var response = await RenameAsync(hat.Organizer.Email, hat.Id, hat.Organizer.Email, "Organizer Renamed");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var stored = await _testDataService.GetHatAsync(hat.Organizer.Email, hat.Id);
        stored.Organizer.Name.Should().Be("Organizer Renamed");
    }

    /// <summary>
    /// The refusal the hierarchy exists for. Two organizers can have the same person in their
    /// exchanges, and only the one who introduced them may say what they are called.
    /// </summary>
    [Fact]
    public async Task EditName_BySomebodyWhoNeitherIsNorAddedThem_ReturnsForbidden()
    {
        // arrange: Alice introduced Bob. Carol then added the same address to hers.
        var alicesHat = await CreateHatWithOrganizerAsync();
        var carolsHat = await CreateHatWithOrganizerAsync();

        var bob = await AddParticipantAsync(alicesHat, "Bob Original");
        await AddParticipantAsync(carolsHat, "Bob Original", bob.Email);

        // act
        var response = await RenameAsync(carolsHat.Organizer.Email, carolsHat.Id, bob.Email, "Bob Renamed");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Forbidden);
        // The refusal explains itself without naming the organizer it is protecting.
        response.Body.Should().NotContain(alicesHat.Organizer.Email);

        var stored = await _testDataService
            .GetParticipantAsync(alicesHat.Organizer.Email, alicesHat.Id, bob.Email);

        stored.Person.Name.Should().Be("Bob Original");
    }

    /// <summary>
    /// The other half of the hierarchy: whoever introduced somebody keeps that standing wherever
    /// they appear, so Alice can still fix Bob's name after Carol has added him to hers.
    /// </summary>
    [Fact]
    public async Task EditName_BySomebodyWhoAddedThem_IsAllowedEverywhereTheyAppear()
    {
        // arrange
        var alicesHat = await CreateHatWithOrganizerAsync();
        var carolsHat = await CreateHatWithOrganizerAsync();

        var bob = await AddParticipantAsync(alicesHat, "Bob Original");
        await AddParticipantAsync(carolsHat, "Bob Original", bob.Email);

        // act
        var response = await RenameAsync(alicesHat.Organizer.Email, alicesHat.Id, bob.Email, "Bob Corrected");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var inCarols = await _testDataService
            .GetParticipantAsync(carolsHat.Organizer.Email, carolsHat.Id, bob.Email);

        inCarols.Person.Name.Should().Be("Bob Corrected");
    }

    /// <summary>
    /// Adding somebody the application already knows does not acquire their name, which is what
    /// stops the hierarchy being walked around by removing and re-adding them.
    /// </summary>
    [Fact]
    public async Task AddingSomebodyAlreadyKnown_DoesNotRenameThem()
    {
        // arrange
        var alicesHat = await CreateHatWithOrganizerAsync();
        var carolsHat = await CreateHatWithOrganizerAsync();

        var bob = await AddParticipantAsync(alicesHat, "Bob Original");

        // act: Carol adds the same address under a name of her choosing.
        await AddParticipantAsync(carolsHat, "Bob As Carol Types It", bob.Email);

        // assert
        var inAlices = await _testDataService
            .GetParticipantAsync(alicesHat.Organizer.Email, alicesHat.Id, bob.Email);

        inAlices.Person.Name.Should().Be("Bob Original");

        // And Carol sees him under the name he already had, not the one she typed.
        var inCarols = await _testDataService
            .GetParticipantAsync(carolsHat.Organizer.Email, carolsHat.Id, bob.Email);

        inCarols.Person.Name.Should().Be("Bob Original");
    }

    [Fact]
    public async Task EditName_ForSomebodyNotInTheExchange_ReturnsNotFound()
    {
        // arrange
        var hat = await CreateHatWithOrganizerAsync();

        // act
        var response = await RenameAsync(hat.Organizer.Email, hat.Id, "nobody@example.com", "Anybody");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Ownership is established by the precondition validator, and a caller who does not organize
    /// this exchange cannot see it at all — so the answer is that there is no such hat rather than
    /// that they may not touch it.
    /// </summary>
    [Fact]
    public async Task EditName_ByAnOrganizerWhoDoesNotOwnTheHat_ReturnsNotFound()
    {
        // arrange
        var alicesHat = await CreateHatWithOrganizerAsync();
        var carolsHat = await CreateHatWithOrganizerAsync();
        var bob = await AddParticipantAsync(alicesHat, "Bob Original");

        // act
        var response = await RenameAsync(carolsHat.Organizer.Email, alicesHat.Id, bob.Email, "Bob Renamed");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);

        var stored = await _testDataService
            .GetParticipantAsync(alicesHat.Organizer.Email, alicesHat.Id, bob.Email);

        stored.Person.Name.Should().Be("Bob Original");
    }

    /// <summary>
    /// Deterministic rather than faked, and unique per call.
    /// </summary>
    /// <remarks>
    /// Every test class in this collection shares one database, and a name is now global to a
    /// person: a faked address that collided with one from another class would give this test
    /// somebody else's participant, carrying somebody else's introducer, and the rename would be
    /// refused for a reason the test never set up. Bogus makes that unlikely; a guid makes it
    /// impossible, and costs nothing here because no assertion cares what the address looks like.
    /// </remarks>
    private static string UniqueEmail(string label) => $"{label}.{Guid.CreateVersion7():N}@example.com";

    private Task<APIGatewayProxyResponse> RenameAsync(
        string organizerEmail,
        Guid hatId,
        string participantEmail,
        string name
    ) =>
        _sut.FunctionHandler(
            _jsonService
                .SerializeDefault(new EditParticipantNameRequest
                {
                    OrganizerEmail = organizerEmail,
                    HatId = hatId,
                    Email = participantEmail,
                    Name = name
                })
                .ToApiGatewayProxyRequest(),
            _context);

    /// <summary>
    /// Mirrors CreateHatService: the organizer is a participant in their own exchange, and both
    /// rows point at the same person.
    /// </summary>
    private async Task<Hat> CreateHatWithOrganizerAsync(string? hatName = null)
    {
        var hat = await CreateTestHatAsync(hatName);

        await _testDataService.CreateParticipantAsync(new AddParticipantRequest
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id,
            Name = hat.Organizer.Name,
            Email = hat.Organizer.Email
        }, []);

        return hat;
    }

    private async Task<Person> AddParticipantAsync(Hat hat, string name, string? email = null)
    {
        var request = new AddParticipantRequest
        {
            OrganizerEmail = hat.Organizer.Email,
            HatId = hat.Id,
            Name = name,
            Email = email ?? UniqueEmail("participant")
        };

        var existing = await _provider.GetParticipantsAsync(hat.Organizer.Email, hat.Id);

        await _testDataService.CreateParticipantAsync(request, existing);

        return new Person { Name = request.Name, Email = request.Email };
    }

    /// <summary>
    /// A hat with an organizer nobody else in the database is, and a name a test can assert on
    /// when it needs to see which exchange a refusal is talking about.
    /// </summary>
    private async Task<Hat> CreateTestHatAsync(string? hatName)
    {
        var data = new HatDataModelFaker().Generate() with
        {
            OrganizerEmail = UniqueEmail("organizer")
        };

        if (hatName is not null)
            // Suffixed so two exchanges by the same label cannot collide, and short enough to stay
            // inside the 50 characters hat.name allows.
            data = data with { HatName = $"{hatName} {Guid.CreateVersion7():N}"[..30] };

        await _testDataService.CreateHatAsync(data);

        return new Hat
        {
            Id = data.HatId,
            Name = data.HatName,
            Status = HatStatus.InProgress,
            AdditionalInformation = data.AdditionalInformation,
            PriceRange = data.PriceRange,
            Organizer = new Person { Email = data.OrganizerEmail, Name = data.OrganizerName },
            Participants = [],
            InvitationsQueuedDate = DateTimeOffset.MinValue,
            ExchangeDate = DateOnly.MinValue
        };
    }
}
