using System.Web;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using GiftExchange.Library.Contexts;
using GiftExchange.Library.Utility;
using Microsoft.Extensions.Logging;
using MimeKit;
using NSubstitute;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// Offering gift ideas about another participant, against a real database.
///
/// Three properties carry the weight, and none of them shows up on the happy path. A GET must send
/// nothing. A subject the sender was not offered must be refused even though the form said so. And
/// the confirmation page must read identically whether or not anything was actually sent — because
/// anything that varied would let somebody map the draw by writing about each participant in turn.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OfferGiftIdeasServiceTests
{
    static OfferGiftIdeasServiceTests()
    {
        DotEnv.Load();
        Environment.SetEnvironmentVariable("LIVE_MODE", "true");
    }

    private readonly IAmazonSimpleEmailService _ses = Substitute.For<IAmazonSimpleEmailService>();

    private readonly IReplyThrottleProvider _throttle = Substitute.For<IReplyThrottleProvider>();

    private readonly IContentModerationService _moderation = Substitute.For<IContentModerationService>();

    private readonly List<byte[]> _sent = [];

    private readonly GiftExchangeProvider _provider;

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly OfferGiftIdeasService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public OfferGiftIdeasServiceTests(PostgresFixture dbFixture)
    {
        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();

        _throttle.TryReserveOfferSlotAsync(Arg.Any<ReserveOfferSlotRequest>())
            .Returns(ReserveSlotResponses.Reserved);

        _moderation.ModerateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ModerationVerdict.Clean);

        _ses.When(ses => ses.SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var buffer = new MemoryStream();
                var data = ((SendRawEmailRequest)call[0]).RawMessage.Data;
                data.Position = 0;
                data.CopyTo(buffer);
                _sent.Add(buffer.ToArray());
            });

        _sut = new OfferGiftIdeasService(
            _provider,
            _throttle,
            new GiftIdeaContentPolicy(),
            _moderation,
            new GiftIdeaEmailCompositionService(),
            new OfferIdeasPageComposer(),
            new AutomaticEmailSender(_ses, Substitute.For<ILogger<AutomaticEmailSender>>()),
            Substitute.For<ILogger<OfferGiftIdeasService>>());
    }

    [Fact]
    public async Task Get_ListsEveryoneButThemselvesAndTheirOwnPick()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Get(exchange.AlphaToken), new FakeLambdaContext());

        // assert: Alpha drew Beta, so Beta is missing -- ideas about your own pick would be routed
        // straight back to you. Alpha is missing because the share page already covers themselves.
        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain(exchange.GammaId.ToString());
        response.Body.Should().Contain(exchange.DeltaId.ToString());
        response.Body.Should().NotContain(exchange.BetaId.ToString());
        response.Body.Should().NotContain(exchange.AlphaId.ToString());
    }

    [Fact]
    public async Task Get_SendsNothingAndSpendsNoSlot()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Get(exchange.AlphaToken), new FakeLambdaContext());

        // assert: the whole reason this is two endpoints. A mail scanner fetching the button in an
        // invitation gets a form and nothing else happens.
        response.Body.Should().Contain("<form method=\"post\"");

        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
        await _throttle.DidNotReceive().TryReserveOfferSlotAsync(Arg.Any<ReserveOfferSlotRequest>());
    }

    [Fact]
    public async Task Get_TicksNobody()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Get(exchange.AlphaToken), new FakeLambdaContext());

        // assert: unlike the Ask, there is no ordinary choice to offer first, so an untouched form
        // is a slip rather than a decision and must not send anything to anybody.
        response.Body.Should().NotContain(" checked");
    }

    [Fact]
    public async Task Post_RoutesToWhoeverDrewTheSubject()
    {
        // arrange
        var exchange = await SeedAsync();

        // act: Alpha writes about Gamma. Beta drew Gamma, so Beta is the only person this is for.
        await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert
        var messages = SentMessages();
        messages.Should().ContainSingle();
        messages[0].To.Mailboxes.Single().Address.Should().Be(exchange.BetaEmail);
        messages[0].HtmlBody.Should().Contain("A cast iron pan.");
    }

    [Fact]
    public async Task Post_NamesTheSenderToTheReaderAndNobodyToTheSender()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: attribution is the point of the feature, and the reader is told. The sender is
        // told nothing about who received it -- not a name, and not an address.
        SentMessages()[0].HtmlBody.Should().Contain("Alpha");

        response.Body.Should().NotContain("Beta");
        response.Body.Should().NotContain(exchange.BetaEmail);
        response.Body.Should().NotContain(exchange.BetaId.ToString());
    }

    [Fact]
    public async Task Post_DoesNotTellTheReaderTheyAsked()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: the contribution forward opens "after you asked", which is false here and false
        // in a way the reader would notice. This path says plainly that nobody asked.
        var body = SentMessages()[0].HtmlBody;
        body.Should().NotContain("after you asked");
        body.Should().Contain("Nobody asked for these");
    }

    [Fact]
    public async Task Post_WarnsTheReaderNotToThankTheSender()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: the one leak unique to this path. The sender wrote unprompted and does not know
        // who received it, so a thank-you would tell them whose name the reader drew. Only the
        // reader can give that away, so only the reader can be warned.
        SentMessages()[0].HtmlBody.Should().Contain("thanking them would tell them whose name you drew");
    }

    [Fact]
    public async Task Post_TellsNeitherTheSubjectNorTheOrganizer()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: Gamma is never told anybody wrote about them, and the organizer sees none of it.
        var recipients = SentMessages().SelectMany(message => message.To.Mailboxes).Select(box => box.Address);
        recipients.Should().NotContain([exchange.GammaEmail, exchange.OrganizerEmail]);
    }

    [Fact]
    public async Task Post_StoresTheOfferWithItsAuthorAndSubject()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: no recipient is recorded, deliberately -- it is resolved when the mail is sent
        // rather than being a fact about the row.
        await using var context = _contextFactory.CreateDbContext();

        var stored = await context.OfferedGiftIdeas
            .Where(offer => offer.AuthorParticipantId == exchange.AlphaId)
            .SingleAsync();

        stored.SubjectParticipantId.Should().Be(exchange.GammaId);
        stored.Ideas.Should().Be("A cast iron pan.");
    }

    [Fact]
    public async Task Post_GivenTheirOwnPickAsSubject_SendsNothing()
    {
        // arrange
        var exchange = await SeedAsync();

        // act: Beta was never offered, so getting here means the form was edited by hand.
        var response = await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.BetaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("Choose who these ideas are about.");
        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
        await AssertNothingStoredAsync(exchange.AlphaId);
    }

    [Fact]
    public async Task Post_GivenASubjectFromAnotherExchange_SendsNothing()
    {
        // arrange
        var exchange = await SeedAsync();
        var other = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.AlphaToken, other.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: membership is checked here rather than assumed from the page having offered it.
        response.Body.Should().Contain("Choose who these ideas are about.");
        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
        await AssertNothingStoredAsync(exchange.AlphaId);
    }

    [Fact]
    public async Task Post_GivenNobodyChosen_AsksAgainAndSendsNothing()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.AlphaToken, Guid.Empty, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: the text survives, so the fix is one click rather than typing it again.
        response.Body.Should().Contain("Choose who these ideas are about.");
        response.Body.Should().Contain("A cast iron pan.");
        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_GivenTheSubjectHasNobodyShoppingForThem_LooksExactlyLikeASend()
    {
        // arrange: two identical exchanges, and in the second nobody holds Gamma's name.
        var sent = await SeedAsync();
        var undeliverable = await SeedAsync();

        await ClearPickAsync(undeliverable.BetaId);

        // act
        var sentResponse = await _sut.FunctionHandler(
            Post(sent.AlphaToken, sent.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        var undeliverableResponse = await _sut.FunctionHandler(
            Post(undeliverable.AlphaToken, undeliverable.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: byte for byte the same page. This is the uniform-confirmation invariant as an
        // executable claim -- anything that differed would let somebody map the draw by writing
        // about each participant in turn and watching what came back.
        undeliverableResponse.Body.Should().Be(sentResponse.Body);

        // ...and only the deliverable one actually went anywhere.
        SentMessages().Should().ContainSingle();
        SentMessages()[0].To.Mailboxes.Single().Address.Should().Be(sent.BetaEmail);
    }

    [Fact]
    public async Task Post_GivenTheSubjectHasNobodyShoppingForThem_StillStoresIt()
    {
        // arrange
        var exchange = await SeedAsync();
        await ClearPickAsync(exchange.BetaId);

        // act
        await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: stored before anything is sent, so a submission that could not be delivered is
        // still what its author wrote rather than nothing at all.
        await using var context = _contextFactory.CreateDbContext();

        (await context.OfferedGiftIdeas.CountAsync(offer => offer.AuthorParticipantId == exchange.AlphaId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Post_GivenInappropriateContent_SpendsNoSlotAndStoresNothing()
    {
        // arrange
        var exchange = await SeedAsync();
        _moderation.ModerateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ModerationVerdict.Toxic);

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "Something unpleasant."),
            new FakeLambdaContext());

        // assert: the deliberate inversion of the Ask's ordering. Somebody has already typed a
        // paragraph by this point, and burning their week on a submission that was then refused
        // would be indefensible.
        await _throttle.DidNotReceive().TryReserveOfferSlotAsync(Arg.Any<ReserveOfferSlotRequest>());

        response.Body.Should().Contain("content we can't pass on");
        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
        await AssertNothingStoredAsync(exchange.AlphaId);
    }

    [Fact]
    public async Task Post_GivenModerationIsUnavailable_SaysSoRatherThanBlamingTheText()
    {
        // arrange
        var exchange = await SeedAsync();
        _moderation.ModerateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ModerationVerdict.Unavailable);

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: the text may be perfectly fine, and calling it inappropriate when the checker was
        // simply unreachable is wrong and unhelpful.
        response.Body.Should().Contain("couldn't check what you wrote");
        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_GivenTheirOwnPicksNameInTheText_IsRefused()
    {
        // arrange
        var exchange = await SeedAsync();

        // act: Alpha drew Beta, and naming Beta in ideas about Gamma would tell the reader whose
        // name Alpha holds.
        var response = await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "Beta said they'd like a cast iron pan."),
            new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("mentions the name of the person you picked");
        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
        await AssertNothingStoredAsync(exchange.AlphaId);
    }

    [Fact]
    public async Task Post_GivenTheyAlreadyWroteAboutThisPerson_SendsNothingAndNamesTheDate()
    {
        // arrange
        var exchange = await SeedAsync();

        _throttle.TryReserveOfferSlotAsync(Arg.Any<ReserveOfferSlotRequest>())
            .Returns(ReserveSlotResponses.RefusedSince(new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero)));

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: their own sharing history, which is theirs already -- unlike the confirmation
        // page, this one may vary, because what varies is not a fact about the draw.
        response.Body.Should().Contain("14 September 2026");
        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
        await AssertNothingStoredAsync(exchange.AlphaId);
    }

    [Fact]
    public async Task Post_HoldsTheSlotPerSubjectRatherThanPerSender()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Post(exchange.AlphaToken, exchange.GammaId, "A cast iron pan."),
            new FakeLambdaContext());

        // assert: keyed on who the ideas are about, never on who receives them. A slot keyed on the
        // recipient would refuse an offer about Delta because of an earlier one about Gamma
        // whenever the two share a giver -- which would say that they do.
        await _throttle.Received(1).TryReserveOfferSlotAsync(Arg.Is<ReserveOfferSlotRequest>(request =>
            request.SharerParticipantId == exchange.AlphaId
            && request.SubjectParticipantId == exchange.GammaId));
    }

    [Fact]
    public async Task GivenAnAskToken_ThePageIsUnavailable()
    {
        // arrange
        var exchange = await SeedAsync();

        var askToken = await _provider.IssueGiftIdeaAskAsync(exchange.AlphaId, exchange.GammaId, exchange.BetaId);

        // act
        var response = await _sut.FunctionHandler(Get(askToken), new FakeLambdaContext());

        // assert: an ask token authorises answering the one ask it was issued for, not starting a
        // conversation about anybody else.
        response.Body.Should().Contain("We can't share gift ideas from this link.");
    }

    [Fact]
    public async Task GivenAFinishedExchange_ThePageIsUnavailable()
    {
        // arrange
        var exchange = await SeedAsync();
        await _provider.UpdateHatStatusAsync(exchange.OrganizerEmail, exchange.HatId, HatStatus.Closed);

        // act
        var response = await _sut.FunctionHandler(Get(exchange.AlphaToken), new FakeLambdaContext());

        // assert: the same page an unknown token gets, so the two cannot be told apart.
        response.Body.Should().Contain("We can't share gift ideas from this link.");
    }

    [Fact]
    public async Task GivenAnUnknownToken_ThePageIsUnavailable()
    {
        // act
        var response = await _sut.FunctionHandler(Get(SecretToken.Create()), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("We can't share gift ideas from this link.");
    }

    private async Task AssertNothingStoredAsync(Guid authorParticipantId)
    {
        await using var context = _contextFactory.CreateDbContext();

        (await context.OfferedGiftIdeas.CountAsync(offer => offer.AuthorParticipantId == authorParticipantId))
            .Should().Be(0);
    }

    /// <summary>Takes away whatever name a participant is holding, leaving their pick undrawn.</summary>
    private async Task ClearPickAsync(Guid participantId)
    {
        await using var context = _contextFactory.CreateDbContext();

        await context.Participants
            .Where(participant => participant.ParticipantId == participantId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(participant => participant.PickedRecipientParticipantId, Guid.Empty));
    }

    private static APIGatewayProxyRequest Get(string token) =>
        new()
        {
            HttpMethod = "GET",
            Resource = "/offer/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token }
        };

    /// <summary>
    /// A URL-encoded post, which is what a form without an enctype sends and what
    /// <see cref="FormBody"/> falls back to. The page itself posts multipart; both are read.
    /// </summary>
    private static APIGatewayProxyRequest Post(string token, Guid subjectId, string ideas) =>
        new()
        {
            HttpMethod = "POST",
            Resource = "/offer/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token },
            Body = subjectId == Guid.Empty
                ? $"{ShareIdeasPageComposer.IdeasField}={HttpUtility.UrlEncode(ideas)}"
                : $"{OfferIdeasPageComposer.SubjectField}={subjectId}&{ShareIdeasPageComposer.IdeasField}={HttpUtility.UrlEncode(ideas)}"
        };

    private ImmutableList<MimeMessage> SentMessages() =>
        [.. _sent.Select(raw => MimeMessage.Load(new MemoryStream(raw)))];

    /// <summary>Alpha drew Beta, Beta drew Gamma, Gamma drew Delta, Delta drew Alpha.</summary>
    /// <remarks>
    /// Four rather than the Ask tests' three, because Alpha needs at least two people they may
    /// write about: with three, excluding themselves and their own pick leaves exactly one name and
    /// nothing to tell "the subject" and "any subject" apart.
    /// </remarks>
    private async Task<SeededExchange> SeedAsync()
    {
        var hat = _hatFaker.Generate();
        await _provider.CreateHatAsync(hat);

        var alpha = await AddAsync(hat, "Alpha");
        var beta = await AddAsync(hat, "Beta");
        var gamma = await AddAsync(hat, "Gamma");
        var delta = await AddAsync(hat, "Delta");

        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, alpha, beta);
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, beta, gamma);
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, gamma, delta);
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, delta, alpha);
        await _provider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.InvitationsSent);

        var tokens = await _provider.IssueGiftIdeaTokensAsync(hat.HatId);

        await using var context = _contextFactory.CreateDbContext();

        var ids = await context.Participants
            .Where(participant => participant.HatId == hat.HatId)
            .Select(participant => new { participant.ParticipantId, participant.Person.Email })
            .ToDictionaryAsync(row => row.Email, row => row.ParticipantId);

        return new SeededExchange(
            hat.HatId,
            hat.OrganizerEmail,
            ids[alpha],
            tokens[alpha],
            ids[beta],
            beta,
            ids[gamma],
            gamma,
            ids[delta]);
    }

    private async Task<string> AddAsync(HatDataModel hat, string name)
    {
        var request = _participantFaker.Generate() with
        {
            HatId = hat.HatId, OrganizerEmail = hat.OrganizerEmail, Name = name
        };

        await _provider.CreateParticipantAsync(request, []);

        return request.Email;
    }

    private sealed record SeededExchange(
        Guid HatId,
        string OrganizerEmail,
        Guid AlphaId,
        string AlphaToken,
        Guid BetaId,
        string BetaEmail,
        Guid GammaId,
        string GammaEmail,
        Guid DeltaId
    );
}
