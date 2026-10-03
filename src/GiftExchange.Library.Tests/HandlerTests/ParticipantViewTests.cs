namespace GiftExchange.Library.Tests.HandlerTests;

/// <summary>
/// The participant's side of an exchange: the list of exchanges somebody is in, and one of them
/// read-only. What matters most here is what does not come back.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ParticipantViewTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly HatDataModelFaker _hatDataModelFaker;

    private readonly AddParticipantRequestFaker _addParticipantRequestFaker;

    private readonly IApiGatewayHandler _view;

    private readonly IApiGatewayHandler _list;

    public ParticipantViewTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _hatDataModelFaker = new HatDataModelFaker();
        _addParticipantRequestFaker = new AddParticipantRequestFaker();

        var contextFactory = dbFixture.CreateContextFactory();
        _context = new FakeLambdaContext();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddValidators()
            .AddSingleton(contextFactory)
            .BuildServiceProvider();

        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _giftExchangeProvider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _view = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("get/participating/{email}/{id}");
        _list = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("get/participating/{email}");
    }

    [Fact]
    public async Task View_GivenInvitationsSent_ShowsEverybodyButOnlyTheCallersPick()
    {
        // arrange
        var source = await CreateDrawnHatAsync();
        await SendInvitationsAsync(source);

        // act
        var response = await ViewAsync(source.Hat.HatId, source.Alpha.Person.Email);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var view = _jsonService.DeserializeDefault<ParticipantView>(response.Body)!;

        view.HatId.Should().Be(source.Hat.HatId);
        view.Name.Should().Be(source.Hat.HatName);
        view.OrganizerName.Should().Be(source.Hat.OrganizerName);
        view.AdditionalInformation.Should().Be(source.Hat.AdditionalInformation);
        view.PriceRange.Should().Be(source.Hat.PriceRange);

        view.Participants.Select(participant => participant.Name).Should().BeEquivalentTo(
            source.Alpha.Person.Name, source.Beta.Person.Name, source.Charlie.Person.Name);

        var you = view.Participants.Should().ContainSingle(participant => participant.IsYou).Subject;
        you.Name.Should().Be(source.Alpha.Person.Name);
        you.PickedRecipient.Should().Be(source.Beta.Person.Name);

        view.Participants
            .Where(participant => !participant.IsYou)
            .Should().AllSatisfy(participant => participant.PickedRecipient.Should().BeEmpty());
    }

    [Fact]
    public async Task View_GivenClosedExchange_ShowsEveryPick()
    {
        // arrange
        var source = await CreateDrawnHatAsync();
        await SendInvitationsAsync(source);
        await _giftExchangeProvider.UpdateHatStatusAsync(source.Hat.OrganizerEmail, source.Hat.HatId, HatStatus.Closed);

        // act
        var response = await ViewAsync(source.Hat.HatId, source.Alpha.Person.Email);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var view = _jsonService.DeserializeDefault<ParticipantView>(response.Body)!;

        view.Participants
            .ToDictionary(participant => participant.Name, participant => participant.PickedRecipient)
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                [source.Alpha.Person.Name] = source.Beta.Person.Name,
                [source.Beta.Person.Name] = source.Charlie.Person.Name,
                [source.Charlie.Person.Name] = source.Alpha.Person.Name
            });
    }

    [Fact]
    public async Task View_NeverCarriesAnybodysAddress()
    {
        // arrange: closed, so every pick is in the body and every name with it.
        var source = await CreateDrawnHatAsync();
        await SendInvitationsAsync(source);
        await _giftExchangeProvider.UpdateHatStatusAsync(source.Hat.OrganizerEmail, source.Hat.HatId, HatStatus.Closed);

        // act
        var response = await ViewAsync(source.Hat.HatId, source.Alpha.Person.Email);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        response.Body.Should().NotContain(source.Hat.OrganizerEmail);
        response.Body.Should().NotContain(source.Alpha.Person.Email);
        response.Body.Should().NotContain(source.Beta.Person.Email);
        response.Body.Should().NotContain(source.Charlie.Person.Email);
    }

    [Theory]
    [InlineData("IN_PROGRESS")]
    [InlineData("READY_FOR_ASSIGNMENT")]
    [InlineData("NAMES_ASSIGNED")]
    public async Task View_GivenInvitationsNotSent_ReturnsNotFound(string status)
    {
        // arrange
        var source = await CreateDrawnHatAsync();
        await _giftExchangeProvider.UpdateHatStatusAsync(source.Hat.OrganizerEmail, source.Hat.HatId, status);

        // act
        var response = await ViewAsync(source.Hat.HatId, source.Alpha.Person.Email);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task View_GivenTheOrganizerWhoIsNotTakingPart_ReturnsNotFound()
    {
        // arrange: the organizer has their own view of this exchange, and this is not it.
        var source = await CreateDrawnHatAsync();
        await SendInvitationsAsync(source);

        // act
        var response = await ViewAsync(source.Hat.HatId, source.Hat.OrganizerEmail);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task View_GivenSomebodyNotInTheExchange_ReturnsNotFound()
    {
        // arrange
        var source = await CreateDrawnHatAsync();
        await SendInvitationsAsync(source);

        // act
        var response = await ViewAsync(source.Hat.HatId, "not.in.it@example.com");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task View_GivenSomebodyWhoLeft_ReturnsNotFound()
    {
        // arrange
        var source = await CreateDrawnHatAsync();
        await SendInvitationsAsync(source);
        await _giftExchangeProvider.DeleteParticipantAsync(
            source.Hat.OrganizerEmail, source.Hat.HatId, source.Charlie.Person.Email);

        // act
        var response = await ViewAsync(source.Hat.HatId, source.Charlie.Person.Email);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task View_GivenAnAddressTheOrganizerTypedInMixedCase_FindsThemByTheirSignIn()
    {
        // arrange: the session carries the address lower-cased; the organizer typed it otherwise.
        var hat = await CreateHatAsync();
        var typed = $"Mixed.Case.{Guid.NewGuid():N}@Example.com";

        var shouty = await AddParticipantAsync(hat, [], email: typed);
        var other = await AddParticipantAsync(hat, [shouty]);
        await PickAsync(hat, shouty, other);
        await PickAsync(hat, other, shouty);
        await _giftExchangeProvider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.NamesAssigned);
        await _giftExchangeProvider.MarkInvitationsAsQueuedAsync(hat.OrganizerEmail, hat.HatId, "127.0.0.1");

        // act
        var response = await ViewAsync(hat.HatId, typed.ToLowerInvariant());

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var view = _jsonService.DeserializeDefault<ParticipantView>(response.Body)!;
        view.Participants.Should().ContainSingle(participant => participant.IsYou)
            .Which.PickedRecipient.Should().Be(other.Person.Name);
    }

    [Fact]
    public async Task List_ShowsOnlyExchangesWhoseInvitationsHaveGoneOut()
    {
        // arrange: the same person in two exchanges, only one of them sent.
        var sent = await CreateDrawnHatAsync();
        await SendInvitationsAsync(sent);

        var unsent = await CreateHatAsync();
        await AddParticipantAsync(unsent, [], email: sent.Alpha.Person.Email);

        // act
        var page = await ListAsync(sent.Alpha.Person.Email, null);

        // assert
        var only = page.Hats.Should().ContainSingle().Subject;
        only.HatId.Should().Be(sent.Hat.HatId);
        only.HatName.Should().Be(sent.Hat.HatName);
        only.OrganizerName.Should().Be(sent.Hat.OrganizerName);
        only.Status.Should().Be(HatStatus.InvitationsSent);
        page.TotalCount.Should().Be(1);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(5);
    }

    [Fact]
    public async Task List_GivenAnOrganizerTakingPartInTheirOwnExchange_IncludesIt()
    {
        // arrange
        var hat = await CreateHatAsync();
        var organizer = await AddParticipantAsync(hat, [], email: hat.OrganizerEmail, name: hat.OrganizerName);
        var other = await AddParticipantAsync(hat, [organizer]);
        await PickAsync(hat, organizer, other);
        await PickAsync(hat, other, organizer);
        await _giftExchangeProvider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.NamesAssigned);
        await _giftExchangeProvider.MarkInvitationsAsQueuedAsync(hat.OrganizerEmail, hat.HatId, "127.0.0.1");

        // act
        var page = await ListAsync(hat.OrganizerEmail, null);

        // assert
        page.Hats.Should().ContainSingle(meta => meta.HatId == hat.HatId);
    }

    [Fact]
    public async Task List_GivenSomebodyInNothing_ReturnsAnEmptyPage()
    {
        // act
        var page = await ListAsync("in.nothing@example.com", null);

        // assert
        page.Hats.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task List_GivenAnInvalidPage_ReturnsBadRequest(string page)
    {
        // arrange
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string> { { "page", page } }
        }.WithAuthenticatedEmail("bad.page@example.com");

        // act
        var response = await _list.FunctionHandler(request, _context);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    private sealed record DrawnHat(HatDataModel Hat, Participant Alpha, Participant Beta, Participant Charlie);

    /// <summary>Three people, drawn in a ring: alpha gives to beta, beta to charlie, charlie to alpha.</summary>
    private async Task<DrawnHat> CreateDrawnHatAsync()
    {
        var hat = await CreateHatAsync();

        var alpha = await AddParticipantAsync(hat, []);
        var beta = await AddParticipantAsync(hat, [alpha]);
        var charlie = await AddParticipantAsync(hat, [alpha, beta]);

        await PickAsync(hat, alpha, beta);
        await PickAsync(hat, beta, charlie);
        await PickAsync(hat, charlie, alpha);

        await _giftExchangeProvider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.NamesAssigned);

        return new DrawnHat(hat, alpha, beta, charlie);
    }

    private Task SendInvitationsAsync(DrawnHat source) =>
        _giftExchangeProvider.MarkInvitationsAsQueuedAsync(source.Hat.OrganizerEmail, source.Hat.HatId, "127.0.0.1");

    private async Task<HatDataModel> CreateHatAsync()
    {
        var hat = _hatDataModelFaker.Generate();
        await _giftExchangeProvider.CreateHatAsync(hat);
        return hat;
    }

    private async Task<Participant> AddParticipantAsync(
        HatDataModel hat,
        ImmutableList<Participant> existingParticipants,
        string? email = null,
        string? name = null
    )
    {
        var request = _addParticipantRequestFaker.Generate() with { HatId = hat.HatId, OrganizerEmail = hat.OrganizerEmail };

        if (email is not null)
            request = request with { Email = email };

        if (name is not null)
            request = request with { Name = name };

        return await _giftExchangeProvider.CreateParticipantAsync(request, existingParticipants);
    }

    private Task PickAsync(HatDataModel hat, Participant giver, Participant recipient) =>
        _giftExchangeProvider.UpdateParticipantPickedRecipientAsync(
            hat.OrganizerEmail, hat.HatId, giver.Person.Email, recipient.Person.Email);

    private Task<APIGatewayProxyResponse> ViewAsync(Guid hatId, string authenticatedEmail) =>
        _view.FunctionHandler(
            new APIGatewayProxyRequest
            {
                PathParameters = new Dictionary<string, string> { { "id", hatId.ToString() } }
            }.WithAuthenticatedEmail(authenticatedEmail),
            _context);

    private async Task<GetParticipatingHatsResponse> ListAsync(string authenticatedEmail, string? page)
    {
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = page is null ? null : new Dictionary<string, string> { { "page", page } }
        }.WithAuthenticatedEmail(authenticatedEmail);

        var response = await _list.FunctionHandler(request, _context);

        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        return _jsonService.DeserializeDefault<GetParticipatingHatsResponse>(response.Body)!;
    }
}
