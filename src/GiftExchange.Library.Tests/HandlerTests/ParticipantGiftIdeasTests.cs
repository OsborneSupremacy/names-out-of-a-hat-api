using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using GiftExchange.Library.Contexts;
using GiftExchange.Library.Extensions;
using MimeKit;
using NSubstitute;

namespace GiftExchange.Library.Tests.HandlerTests;

/// <summary>
/// What the email links do, from the participant's own signed-in page, end to end against a real
/// database.
/// </summary>
/// <remarks>
/// The rules themselves are pinned down by the tests of the email pages, which share the same
/// workflows. What is pinned down here is the other half: that the signed-in door finds the right
/// participant and nobody else, and that what the page shows back is only what has already reached
/// the person reading it — above all, that a held submission stays held and an ask never names its
/// asker.
///
/// Built from the real service collection, with only the parts that would reach AWS swapped out.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ParticipantGiftIdeasTests
{
    static ParticipantGiftIdeasTests()
    {
        DotEnv.Load();
        Environment.SetEnvironmentVariable("LIVE_MODE", "true");
    }

    private readonly IAmazonSimpleEmailService _ses = Substitute.For<IAmazonSimpleEmailService>();

    private readonly IReplyThrottleProvider _throttle = Substitute.For<IReplyThrottleProvider>();

    private readonly IContentModerationService _moderation = Substitute.For<IContentModerationService>();

    private readonly IEmailQueue _queue = Substitute.For<IEmailQueue>();

    private readonly List<GiftExchangeEmailRequest> _queued = [];

    /// <summary>Raw MIME captured as each send happens, because the sender disposes the buffer it wrote.</summary>
    private readonly List<byte[]> _sent = [];

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly GiftExchangeProvider _provider;

    private readonly JsonService _jsonService;

    private readonly IServiceProvider _services;

    private readonly ILambdaContext _context = new FakeLambdaContext();

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public ParticipantGiftIdeasTests(PostgresFixture dbFixture)
    {
        _contextFactory = dbFixture.CreateContextFactory();

        _services = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddValidators()
            .AddSingleton(_contextFactory)
            // Registered after the real ones, so these are what gets resolved.
            .AddSingleton(_ses)
            .AddSingleton(_throttle)
            .AddSingleton(_moderation)
            .AddSingleton(_queue)
            .BuildServiceProvider();

        _provider = _services.GetRequiredService<GiftExchangeProvider>();
        _jsonService = _services.GetRequiredService<JsonService>();

        _throttle.TryReserveAskSlotAsync(Arg.Any<ReserveAskSlotRequest>()).Returns(ReserveSlotResponses.Reserved);
        _throttle.TryReserveOfferSlotAsync(Arg.Any<ReserveOfferSlotRequest>()).Returns(ReserveSlotResponses.Reserved);

        _moderation.ModerateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ModerationVerdict.Clean);

        _queue.EnqueueAsync(Arg.Do<GiftExchangeEmailRequest>(email => _queued.Add(email)))
            .Returns(Task.CompletedTask);

        _ses.When(ses => ses.SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var buffer = new MemoryStream();
                var data = ((SendRawEmailRequest)call[0]).RawMessage.Data;
                data.Position = 0;
                data.CopyTo(buffer);
                _sent.Add(buffer.ToArray());
            });
    }

    [Fact]
    public async Task View_OffersEverythingTheEmailDoes()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var view = await ViewAsync(exchange, exchange.Alpha);

        // assert
        view.CanLeave.Should().BeTrue();

        var ideas = view.GiftIdeas;
        ideas.CanShare.Should().BeTrue();
        ideas.CanAsk.Should().BeTrue();

        // Their pick first and marked; everybody else after.
        ideas.AskCandidates.Select(candidate => (candidate.ParticipantId, candidate.IsTheirPick))
            .Should().Equal((exchange.Beta.Id, true), (exchange.Gamma.Id, false));

        // Nobody can offer ideas about their own pick, which would route back to themselves.
        ideas.OfferCandidates.Select(candidate => candidate.ParticipantId)
            .Should().Equal(exchange.Gamma.Id);
    }

    [Fact]
    public async Task View_GivenTheOrganizerTakingPart_DoesNotOfferToLeave()
    {
        // arrange
        var exchange = await SeedAsync(organizerTakesPart: true);

        // act
        var view = await ViewAsync(exchange, exchange.Alpha);

        // assert: in Alpha's place is the organizer.
        view.CanLeave.Should().BeFalse();
        view.GiftIdeas.CanShare.Should().BeTrue();
    }

    [Fact]
    public async Task View_GivenAClosedExchange_ShowsWhatWasSharedButOffersNothingToDo()
    {
        // arrange
        var exchange = await SeedAsync();
        (await ShareAsync(exchange, exchange.Alpha, "A scarf")).StatusCode.Should().Be(204);
        await _provider.UpdateHatStatusAsync(exchange.OrganizerEmail, exchange.HatId, HatStatus.Closed);

        // act
        var view = await ViewAsync(exchange, exchange.Alpha);

        // assert
        view.GiftIdeas.YourIdeas.Should().Be("A scarf");
        view.GiftIdeas.CanShare.Should().BeFalse();
        view.GiftIdeas.CanAsk.Should().BeFalse();
        view.GiftIdeas.AskCandidates.Should().BeEmpty();
        view.GiftIdeas.OfferCandidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Share_StoresAndForwardsToWhoeverDrewThem()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await ShareAsync(exchange, exchange.Alpha, "A scarf\r\nor gloves");

        // assert: Gamma drew Alpha.
        response.StatusCode.Should().Be(204);

        var forward = SentMessages().Should().ContainSingle().Subject;
        forward.To.Mailboxes.Single().Address.Should().Be(exchange.Gamma.Email);

        var yours = (await ViewAsync(exchange, exchange.Alpha)).GiftIdeas;
        yours.YourIdeas.Should().Be("A scarf\nor gloves");
        yours.HoldUntilAsked.Should().BeFalse();

        var givers = (await ViewAsync(exchange, exchange.Gamma)).GiftIdeas;
        givers.FromYourPick.Should().Be("A scarf\nor gloves");
    }

    [Fact]
    public async Task Share_GivenHeldIdeas_ShowsThemToNobodyUntilTheGiverAsks()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await ShareAsync(exchange, exchange.Alpha, "A scarf", holdUntilAsked: true);

        // assert: stored, sent nowhere, and not on the giver's page either.
        response.StatusCode.Should().Be(204);
        _sent.Should().BeEmpty();

        (await ViewAsync(exchange, exchange.Alpha)).GiftIdeas.HoldUntilAsked.Should().BeTrue();
        (await ViewAsync(exchange, exchange.Gamma)).GiftIdeas.FromYourPick.Should().BeEmpty();

        // and once Gamma asks, both the email and the page have it.
        var asked = await AskAsync(exchange, exchange.Gamma, null, exchange.Alpha.Id);
        asked.StatusCode.Should().Be(200);
        _jsonService.DeserializeDefault<AskForGiftIdeasResponse>(asked.Body)!.ReleasedHeldIdeas.Should().BeTrue();

        (await ViewAsync(exchange, exchange.Gamma)).GiftIdeas.FromYourPick.Should().Be("A scarf");
    }

    [Fact]
    public async Task Share_GivenTheirPicksName_RefusesInPlainWords()
    {
        // arrange
        var exchange = await SeedAsync();

        // act: Alpha drew Beta.
        var response = await ShareAsync(exchange, exchange.Alpha, "Same as Beta, please");

        // assert: the email page's words, decoded out of HTML.
        response.StatusCode.Should().Be((int)HttpStatusCode.UnprocessableEntity);
        response.Body.Should().Contain("mentions the name of the person you picked");
        response.Body.Should().NotContain("&mdash;");
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Share_GivenAClosedExchange_ReturnsConflict()
    {
        // arrange
        var exchange = await SeedAsync();
        await _provider.UpdateHatStatusAsync(exchange.OrganizerEmail, exchange.HatId, HatStatus.Closed);

        // act
        var response = await ShareAsync(exchange, exchange.Alpha, "A scarf");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Share_NamingSomebodyElseInTheBody_ActsOnlyAsTheSignedInCaller()
    {
        // arrange
        var exchange = await SeedAsync();
        var body = _jsonService.SerializeDefault(new ShareGiftIdeasRequest
        {
            ParticipantEmail = exchange.Alpha.Email,
            HatId = exchange.HatId,
            Ideas = "Written by somebody else",
            HoldUntilAsked = false
        });

        // act: signed in as somebody who is not in the exchange, claiming to be Alpha.
        var response = await SendAsync("PUT", "/participating/ideas", body, "not.in.it@example.com");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        (await ViewAsync(exchange, exchange.Alpha)).GiftIdeas.YourIdeas.Should().BeEmpty();
    }

    [Fact]
    public async Task Ask_ThenAnswer_ReachesTheAskerWithoutNamingThemToTheHelper()
    {
        // arrange: Alpha drew Beta, and asks Gamma about her.
        var exchange = await SeedAsync();

        var asked = await AskAsync(exchange, exchange.Alpha, "What size?", exchange.Gamma.Id);
        asked.StatusCode.Should().Be(200);

        // act: Gamma sees the ask on their own page and answers it there.
        var helpers = (await ViewAsync(exchange, exchange.Gamma)).GiftIdeas;
        var ask = helpers.AsksForYou.Should().ContainSingle().Subject;

        var answered = await AnswerAsync(exchange, exchange.Gamma, ask.AskId, "Medium, and she likes green");

        // assert: the ask names who it is about, and nothing in it names who asked.
        ask.SubjectName.Should().Be("Beta");
        ask.YourAnswer.Should().BeEmpty();
        _jsonService.SerializeDefault(ask).Should().NotContain("Alpha");

        answered.StatusCode.Should().Be(204);

        var askers = (await ViewAsync(exchange, exchange.Alpha)).GiftIdeas;
        var suggestion = askers.AboutYourPick.Should().ContainSingle().Subject;
        suggestion.From.Should().Be("Gamma");
        suggestion.Ideas.Should().Be("Medium, and she likes green");
        suggestion.WasAskedFor.Should().BeTrue();
        askers.AskedAboutYourPick.Should().ContainSingle()
            .Which.Should().Match<AskedHelper>(helper => helper.Name == "Gamma" && helper.HasAnswered);

        (await ViewAsync(exchange, exchange.Gamma)).GiftIdeas.AsksForYou.Single().YourAnswer
            .Should().Be("Medium, and she likes green");
    }

    [Fact]
    public async Task Answer_GivenSomebodyElsesAsk_ReturnsNotFound()
    {
        // arrange: the ask was put to Gamma.
        var exchange = await SeedAsync();
        await AskAsync(exchange, exchange.Alpha, null, exchange.Gamma.Id);
        var ask = (await ViewAsync(exchange, exchange.Gamma)).GiftIdeas.AsksForYou.Single();
        _sent.Clear();

        // act: Beta, who is the subject, tries to answer it.
        var response = await AnswerAsync(exchange, exchange.Beta, ask.AskId, "I'd like a pony");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Ask_GivenNobody_ReturnsBadRequest()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await AskAsync(exchange, exchange.Alpha, null);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Offer_ReachesWhoeverDrewThemAndShowsOnTheAuthorsPage()
    {
        // arrange: Beta drew Gamma, and knows what Alpha would like. Gamma drew Alpha.
        var exchange = await SeedAsync();

        // act
        var response = await OfferAsync(exchange, exchange.Beta, exchange.Alpha.Id, "Anything with owls");

        // assert
        response.StatusCode.Should().Be(204);
        SentMessages().Should().ContainSingle()
            .Which.To.Mailboxes.Single().Address.Should().Be(exchange.Gamma.Email);

        var authors = (await ViewAsync(exchange, exchange.Beta)).GiftIdeas;
        authors.YourOffers.Should().ContainSingle()
            .Which.Should().Match<YourOfferedGiftIdeas>(offer =>
                offer.SubjectName == "Alpha" && offer.Ideas == "Anything with owls");

        var givers = (await ViewAsync(exchange, exchange.Gamma)).GiftIdeas;
        givers.AboutYourPick.Should().ContainSingle()
            .Which.Should().Match<SuggestedGiftIdeas>(suggestion =>
                suggestion.From == "Beta" && !suggestion.WasAskedFor);
    }

    [Fact]
    public async Task Offer_AboutTheirOwnPick_ReturnsBadRequest()
    {
        // arrange: Alpha drew Beta.
        var exchange = await SeedAsync();

        // act
        var response = await OfferAsync(exchange, exchange.Alpha, exchange.Beta.Id, "Owls");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Offer_WithinTheWeek_ReturnsTooManyRequestsAndStoresNothing()
    {
        // arrange
        var exchange = await SeedAsync();
        _throttle.TryReserveOfferSlotAsync(Arg.Any<ReserveOfferSlotRequest>())
            .Returns(ReserveSlotResponses.RefusedSince(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)));

        // act
        var response = await OfferAsync(exchange, exchange.Beta, exchange.Alpha.Id, "Owls");

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.TooManyRequests);
        response.Body.Should().Contain("1 October 2026");
        (await ViewAsync(exchange, exchange.Beta)).GiftIdeas.YourOffers.Should().BeEmpty();
    }

    [Fact]
    public async Task Leave_RemovesThemSendsTheExchangeBackAndRecordsTheRefusal()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await LeaveAsync(exchange, exchange.Gamma, blockOrganizer: true);

        // assert
        response.StatusCode.Should().Be(204);

        (await ViewResponseAsync(exchange, exchange.Gamma)).StatusCode.Should().Be((int)HttpStatusCode.NotFound);

        var (_, hat) = await _provider.GetHatAsync(exchange.OrganizerEmail, exchange.HatId);
        hat.Status.Should().Be(HatStatus.InProgress);
        hat.Participants.Should().NotContain(participant => participant.Person.Email == exchange.Gamma.Email);

        _queued.Should().Contain(email => email.MessageType == EmailMessageType.OrganizerParticipantLeft);

        var blocked = await _provider.FindBlockedByOrganizerAsync(
            [exchange.Gamma.Email.ToMailboxKey()], exchange.OrganizerEmail.ToMailboxKey());
        blocked.Should().ContainSingle();
    }

    [Fact]
    public async Task Leave_GivenTheOrganizer_ReturnsNotFoundAndRemovesNobody()
    {
        // arrange
        var exchange = await SeedAsync(organizerTakesPart: true);

        // act: in Alpha's place is the organizer.
        var response = await LeaveAsync(exchange, exchange.Alpha, blockOrganizer: false);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);

        var (_, hat) = await _provider.GetHatAsync(exchange.OrganizerEmail, exchange.HatId);
        hat.Participants.Should().HaveCount(3);
        _queued.Should().BeEmpty();
    }

    private async Task<ParticipantView> ViewAsync(SeededExchange exchange, Seeded who)
    {
        var response = await ViewResponseAsync(exchange, who);
        response.StatusCode.Should().Be(200);
        return _jsonService.DeserializeDefault<ParticipantView>(response.Body)!;
    }

    private Task<APIGatewayProxyResponse> ViewResponseAsync(SeededExchange exchange, Seeded who) =>
        _services.GetRequiredKeyedService<IApiGatewayHandler>("get/participating/{email}/{id}")
            .FunctionHandler(
                new APIGatewayProxyRequest
                {
                    PathParameters = new Dictionary<string, string> { { "id", exchange.HatId.ToString() } }
                }.WithAuthenticatedEmail(who.Email),
                _context);

    private Task<APIGatewayProxyResponse> ShareAsync(
        SeededExchange exchange,
        Seeded who,
        string ideas,
        bool holdUntilAsked = false
    ) =>
        SendAsync("PUT", "/participating/ideas", _jsonService.SerializeDefault(new ShareGiftIdeasRequest
        {
            ParticipantEmail = who.Email,
            HatId = exchange.HatId,
            Ideas = ideas,
            HoldUntilAsked = holdUntilAsked
        }), who.Email);

    private Task<APIGatewayProxyResponse> AnswerAsync(SeededExchange exchange, Seeded who, Guid askId, string ideas) =>
        SendAsync("PUT", "/participating/answer", _jsonService.SerializeDefault(new AnswerGiftIdeaAskRequest
        {
            ParticipantEmail = who.Email,
            HatId = exchange.HatId,
            AskId = askId,
            Ideas = ideas
        }), who.Email);

    private Task<APIGatewayProxyResponse> OfferAsync(SeededExchange exchange, Seeded who, Guid subjectId, string ideas) =>
        SendAsync("POST", "/participating/offer", _jsonService.SerializeDefault(new OfferGiftIdeasRequest
        {
            ParticipantEmail = who.Email,
            HatId = exchange.HatId,
            SubjectParticipantId = subjectId,
            Ideas = ideas
        }), who.Email);

    private Task<APIGatewayProxyResponse> AskAsync(SeededExchange exchange, Seeded who, string? question, params Guid[] chosen) =>
        SendAsync("POST", "/participating/ask", _jsonService.SerializeDefault(new AskForGiftIdeasRequest
        {
            ParticipantEmail = who.Email,
            HatId = exchange.HatId,
            ParticipantIds = [.. chosen],
            Question = question ?? string.Empty
        }), who.Email);

    private Task<APIGatewayProxyResponse> LeaveAsync(SeededExchange exchange, Seeded who, bool blockOrganizer) =>
        SendAsync("POST", "/participating/leave", _jsonService.SerializeDefault(new LeaveGiftExchangeRequest
        {
            ParticipantEmail = who.Email,
            HatId = exchange.HatId,
            BlockOrganizer = blockOrganizer,
            BlockAnywhere = false
        }), who.Email);

    private Task<APIGatewayProxyResponse> SendAsync(string method, string resource, string body, string authenticatedEmail) =>
        _services.GetRequiredKeyedService<IApiGatewayHandler>($"{method}{resource}".ToLowerInvariant())
            .FunctionHandler(
                new APIGatewayProxyRequest
                {
                    HttpMethod = method,
                    Resource = resource,
                    Body = body
                }.WithAuthenticatedEmail(authenticatedEmail),
                _context);

    private ImmutableList<MimeMessage> SentMessages() =>
        [.. _sent.Select(raw => MimeMessage.Load(new MemoryStream(raw)))];

    /// <summary>
    /// Alpha drew Beta, Beta drew Gamma, Gamma drew Alpha, with invitations out. Optionally with
    /// the organizer in Alpha's place, taking part in their own exchange.
    /// </summary>
    private async Task<SeededExchange> SeedAsync(bool organizerTakesPart = false)
    {
        var hat = _hatFaker.Generate();
        await _provider.CreateHatAsync(hat);

        var alphaEmail = organizerTakesPart
            ? await AddAsync(hat, hat.OrganizerName, hat.OrganizerEmail)
            : await AddAsync(hat, "Alpha", null);
        var betaEmail = await AddAsync(hat, "Beta", null);
        var gammaEmail = await AddAsync(hat, "Gamma", null);

        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, alphaEmail, betaEmail);
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, betaEmail, gammaEmail);
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, gammaEmail, alphaEmail);
        await _provider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.InvitationsSent);

        await using var context = _contextFactory.CreateDbContext();

        // As stored, which is what a session carries.
        var people = await context.Participants
            .Where(participant => participant.HatId == hat.HatId)
            .Select(participant => new { participant.ParticipantId, participant.Person.Email })
            .ToDictionaryAsync(row => row.Email, row => new Seeded(row.ParticipantId, row.Email));

        Seeded Find(string email) => people[email.ToLowerInvariant()];

        return new SeededExchange(hat.HatId, hat.OrganizerEmail, Find(alphaEmail), Find(betaEmail), Find(gammaEmail));
    }

    private async Task<string> AddAsync(HatDataModel hat, string name, string? email)
    {
        var request = _participantFaker.Generate() with
        {
            HatId = hat.HatId, OrganizerEmail = hat.OrganizerEmail, Name = name
        };

        if (email is not null)
            request = request with { Email = email };

        await _provider.CreateParticipantAsync(request, []);

        return request.Email;
    }

    private sealed record Seeded(Guid Id, string Email);

    private sealed record SeededExchange(Guid HatId, string OrganizerEmail, Seeded Alpha, Seeded Beta, Seeded Gamma);
}
