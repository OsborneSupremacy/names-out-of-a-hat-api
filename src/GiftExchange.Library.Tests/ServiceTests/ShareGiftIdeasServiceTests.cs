using System.Text;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using GiftExchange.Library.Contexts;
using GiftExchange.Library.Utility;
using Microsoft.Extensions.Logging;
using MimeKit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The share page end to end, against a real database.
///
/// The properties worth pinning down are the ones a later refactor could rearrange without noticing:
/// that a GET never stores or sends anything, that a refusal keeps what was written and sends
/// nothing, and that each kind of submission is stored in its own table and goes to its own person.
///
/// The provider is the real one, because what is being exercised is a token resolving to a
/// participant, their pick, and whoever drew them. A stubbed lookup would only assert that the test
/// knows its own arrangement.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ShareGiftIdeasServiceTests
{
    static ShareGiftIdeasServiceTests()
    {
        DotEnv.Load();
        Environment.SetEnvironmentVariable("LIVE_MODE", "true");
    }

    private readonly IAmazonSimpleEmailService _ses = Substitute.For<IAmazonSimpleEmailService>();

    private readonly IContentModerationService _moderation = Substitute.For<IContentModerationService>();

    /// <summary>
    /// Raw MIME captured as each send happens, because the sender disposes the buffer it wrote.
    /// </summary>
    private readonly List<byte[]> _sent = [];

    private readonly GiftExchangeProvider _provider;

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly ShareGiftIdeasService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public ShareGiftIdeasServiceTests(PostgresFixture dbFixture)
    {
        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();

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

        _sut = new ShareGiftIdeasService(
            _provider,
            new GiftIdeaContentPolicy(),
            _moderation,
            new GiftIdeaEmailCompositionService(),
            new ShareIdeasPageComposer(),
            new AutomaticEmailSender(_ses, Substitute.For<ILogger<AutomaticEmailSender>>()),
            Substitute.For<ILogger<ShareGiftIdeasService>>());
    }

    [Fact]
    public async Task Get_RendersAnEmptyFormAndDoesNothingElse()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Get(exchange.Token), new FakeLambdaContext());

        // assert: the whole reason this is two endpoints. A mail scanner following the button gets
        // this page, and nothing is stored or forwarded on anybody's behalf.
        response.StatusCode.Should().Be(200);
        response.Headers["Content-Type"].Should().StartWith("text/html");
        response.Body.Should().Contain("<form method=\"post\" enctype=\"multipart/form-data\"");
        response.Body.Should().Contain($"action=\"https://api.namesoutofahat.com/ideas/{exchange.Token}\"");

        _sent.Should().BeEmpty();
        await ShouldHaveStoredNothing(exchange);
    }

    [Fact]
    public async Task Get_NeverNamesTheParticipantsOwnPick()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Get(exchange.Token), new FakeLambdaContext());

        // assert: the link is the only credential, so whoever holds it sees this page, and the pick
        // is the one thing this application keeps quiet.
        response.Body.Should().NotContain("Beta");
    }

    [Fact]
    public async Task Get_FillsTheFormWithWhatWasSharedLast()
    {
        // arrange
        var exchange = await SeedAsync();
        await ShareOutrightAsync(exchange.AlphaId, "A scarf");
        await ShareOutrightAsync(exchange.AlphaId, "Actually, a <b>bread</b> book");

        // act
        var response = await _sut.FunctionHandler(Get(exchange.Token), new FakeLambdaContext());

        // assert: the newest, and encoded, because a participant wrote it.
        response.Body.Should().Contain("Actually, a &lt;b&gt;bread&lt;/b&gt; book");
        response.Body.Should().NotContain("A scarf");
        response.Body.Should().Contain("what you shared last time");
    }

    [Theory]
    [InlineData("IN_PROGRESS")]
    [InlineData("CLOSED")]
    public async Task AnyMethod_GivenAnExchangeNotTakingIdeas_ShowsTheUnavailablePage(string status)
    {
        // arrange
        var exchange = await SeedAsync();
        await _provider.UpdateHatStatusAsync(exchange.OrganizerEmail, exchange.HatId, status);

        // act
        var get = await _sut.FunctionHandler(Get(exchange.Token), new FakeLambdaContext());
        var post = await _sut.FunctionHandler(Post(exchange.Token, "A scarf."), new FakeLambdaContext());

        // assert
        get.Body.Should().Be(ShareIdeasPageComposer.ComposeUnavailable());
        post.Body.Should().Be(ShareIdeasPageComposer.ComposeUnavailable());

        _sent.Should().BeEmpty();
        await ShouldHaveStoredNothing(exchange);
    }

    [Fact]
    public async Task AnyMethod_GivenAnUnknownToken_ShowsTheSameUnavailablePage()
    {
        // act
        var response = await _sut.FunctionHandler(Post("not-a-real-token", "A scarf."), new FakeLambdaContext());

        // assert: identical to a finished exchange, so a guessed token learns nothing.
        response.StatusCode.Should().Be(200);
        response.Body.Should().Be(ShareIdeasPageComposer.ComposeUnavailable());
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_GivenGoodIdeas_StoresThemAndForwardsThemToWhoeverDrewTheSender()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.Token, "A cast iron skillet, please."), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("Shared!");
        response.Body.Should().Contain("A cast iron skillet, please.");

        await using var context = _contextFactory.CreateDbContext();

        var stored = await context.GiftIdeas
            .Where(giftIdea => giftIdea.ParticipantId == exchange.AlphaId)
            .ToListAsync();

        stored.Should().ContainSingle().Which.Ideas.Should().Be("A cast iron skillet, please.");
        stored.Single().InboundMessageId.Should().BeEmpty();

        // Gamma drew Alpha. Nothing goes back to Alpha: the page is the confirmation now.
        var forward = SentMessages().Should().ContainSingle().Subject;
        forward.To.Mailboxes.Single().Address.Should().Be(exchange.GammaEmail);
        forward.HtmlBody.Should().Contain("A cast iron skillet, please.");
    }

    [Fact]
    public async Task Post_GivesTheForwardNoWayBackToTheSender()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(Post(exchange.Token, "A scarf."), new FakeLambdaContext());

        // assert: a reply that reached the sender would tell them who holds their name.
        var forward = SentMessages().Single();

        forward.ReplyTo.Count.Should().Be(0, "a reply must have nowhere to go");
        forward.From.Mailboxes.Single().Address.Should().Be("donotreply@mail.namesoutofahat.com");

        // Named, because the recipient already knows whose name they drew.
        forward.HtmlBody.Should().Contain("Alpha");
    }

    [Fact]
    public async Task Post_StoresBeforeItSends()
    {
        // arrange: if sending fails after the write, the submission still exists. The other order
        // loses what somebody wrote.
        var exchange = await SeedAsync();
        _ses.SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonSimpleEmailServiceException("SES is unavailable"));

        // act
        var response = await _sut.FunctionHandler(Post(exchange.Token, "A scarf."), new FakeLambdaContext());

        // assert
        response.StatusCode.Should().Be(200);

        await using var context = _contextFactory.CreateDbContext();
        (await context.GiftIdeas.AnyAsync(row => row.ParticipantId == exchange.AlphaId)).Should().BeTrue();
    }

    [Fact]
    public async Task Post_GivenTextNamingTheirPick_RefusesItAndKeepsWhatWasWritten()
    {
        // arrange: Alpha drew Beta, and Beta's giver is not who this goes to — but Gamma, who does
        // receive it, would learn who Alpha drew.
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.Token, "Something Beta would also like"), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("mentions the name of the person you picked");
        response.Body.Should().Contain(">Something Beta would also like</textarea>");

        _sent.Should().BeEmpty();
        await ShouldHaveStoredNothing(exchange);
    }

    [Theory]
    [InlineData("Try https://bit.ly/abc", "shortened link")]
    [InlineData("Sign in at https://namesoutofahat.com/auth", "links back to namesoutofahat.com")]
    [InlineData("   ", "Write your ideas in the box")]
    public async Task Post_GivenTextThePolicyRefuses_SaysWhyAndSendsNothing(string ideas, string explanation)
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Post(exchange.Token, ideas), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain(explanation);
        response.Body.Should().Contain("<form method=\"post\" enctype=\"multipart/form-data\"");

        _sent.Should().BeEmpty();
        await ShouldHaveStoredNothing(exchange);
    }

    [Fact]
    public async Task Post_GivenModerationRefusesIt_DoesNotStoreOrForward()
    {
        // arrange
        var exchange = await SeedAsync();
        _moderation.ModerateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ModerationVerdict.Toxic);

        // act
        var response = await _sut.FunctionHandler(Post(exchange.Token, "Something unpleasant."), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("content we can't pass on");
        _sent.Should().BeEmpty();
        await ShouldHaveStoredNothing(exchange);
    }

    [Fact]
    public async Task Post_GivenModerationIsUnavailable_AsksThemToTryAgainRatherThanReword()
    {
        // arrange
        var exchange = await SeedAsync();
        _moderation.ModerateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ModerationVerdict.Unavailable);

        // act
        var response = await _sut.FunctionHandler(Post(exchange.Token, "A cast iron skillet."), new FakeLambdaContext());

        // assert: still refused, because nothing unchecked is forwarded. But the text may be fine,
        // so the page must not call it inappropriate, and it keeps the text for the retry.
        response.Body.Should().Contain("try again in a few minutes");
        response.Body.Should().NotContain("content we can't pass on");
        response.Body.Should().Contain(">A cast iron skillet.</textarea>");

        _sent.Should().BeEmpty();
        await ShouldHaveStoredNothing(exchange);
    }

    [Fact]
    public async Task Post_ChecksThePolicyBeforeModeration()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(Post(exchange.Token, "https://bit.ly/abc"), new FakeLambdaContext());

        // assert: text refused on a rule this application can apply itself never reaches Comprehend.
        await _moderation.DidNotReceive().ModerateAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Post_NormalisesLineEndingsAndTrims()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(Post(exchange.Token, "  A scarf\r\nA hat\r\n  "), new FakeLambdaContext());

        // assert: browsers post a textarea's breaks as CRLF.
        (await _provider.GetLatestGiftIdeaAsync(exchange.AlphaId)).Ideas.Should().Be("A scarf\nA hat");
    }

    [Fact]
    public async Task Post_GivenAUrlEncodedBody_ReadsItTheSame()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(PostUrlEncoded(exchange.Token, "A scarf & a hat."), new FakeLambdaContext());

        // assert
        (await _provider.GetLatestGiftIdeaAsync(exchange.AlphaId)).Ideas.Should().Be("A scarf & a hat.");
    }

    [Fact]
    public async Task Post_ReadsNonAsciiTextAsUtf8()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(Post(exchange.Token, "Crème brûlée torch 🎁 漢字"), new FakeLambdaContext());

        // assert: a multipart part names no charset, and the page declares UTF-8.
        (await _provider.GetLatestGiftIdeaAsync(exchange.AlphaId)).Ideas.Should().Be("Crème brûlée torch 🎁 漢字");
    }

    [Fact]
    public async Task Post_GivenTextOverTheLimit_SaysHowLongItMayBeAndKeepsIt()
    {
        // arrange
        var exchange = await SeedAsync();
        var ideas = new string('a', GiftIdeaContentPolicy.MaxLength + 1);

        // act
        var response = await _sut.FunctionHandler(Post(exchange.Token, ideas), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("2,000 characters or fewer");
        response.Body.Should().Contain($">{ideas}</textarea>");
        _sent.Should().BeEmpty();
        await ShouldHaveStoredNothing(exchange);
    }

    [Fact]
    public async Task Get_CapsTheBoxAtTheLimitAndPostsMultipart()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Get(exchange.Token), new FakeLambdaContext());

        // assert: both halves of staying under the firewall's 8 KB body limit.
        response.Body.Should().Contain($"maxlength=\"{GiftIdeaContentPolicy.MaxLength}\"");
        response.Body.Should().Contain("enctype=\"multipart/form-data\"");
    }

    [Fact]
    public async Task Post_GivenABase64EncodedBody_ReadsItTheSame()
    {
        // arrange
        var exchange = await SeedAsync();
        var request = Post(exchange.Token, "A scarf.");
        request.Body = Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Body));
        request.IsBase64Encoded = true;

        // act
        await _sut.FunctionHandler(request, new FakeLambdaContext());

        // assert
        (await _provider.GetLatestGiftIdeaAsync(exchange.AlphaId)).Ideas.Should().Be("A scarf.");
    }

    [Fact]
    public async Task Post_GivenAContribution_SendsItToTheAskerAndNotToTheirOwnGiver()
    {
        // arrange: Alpha drew Beta and asked Gamma what Beta might like.
        var exchange = await SeedAsync();
        var ask = await GammaAskedAboutBetaAsync(exchange);

        // act
        var response = await _sut.FunctionHandler(
            Post(ask.Token, "Beta has been after a stand mixer."), new FakeLambdaContext());

        // assert: Beta drew Gamma, so anything Gamma writes about themselves goes to Beta — but this
        // is not about Gamma, and sending it to Beta would hand Beta their own shopping list.
        response.Body.Should().Contain("They'll see that these came from you");

        var forward = SentMessages().Should().ContainSingle().Subject;
        forward.To.Mailboxes.Single().Address.Should().Be(exchange.AlphaEmail);
        forward.Subject.Should().Contain("Gamma").And.Contain("Beta");
        forward.HtmlBody.Should().Contain("stand mixer");
    }

    [Fact]
    public async Task Post_GivenAContribution_StoresItApartFromTheirOwnWishes()
    {
        // arrange
        var exchange = await SeedAsync();
        var ask = await GammaAskedAboutBetaAsync(exchange);

        // act
        await _sut.FunctionHandler(Post(ask.Token, "Beta has been after a stand mixer."), new FakeLambdaContext());

        // assert: filed against the ask, not against Gamma. A row in gift_idea would be Gamma saying
        // what Gamma wants.
        (await _provider.GetLatestContributedGiftIdeaAsync(ask.AskId))
            .Should().Be("Beta has been after a stand mixer.");

        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeas.AnyAsync(row => row.ParticipantId == exchange.GammaId))
            .Should().BeFalse("a suggestion about somebody else is not a wish of their own");
    }

    [Fact]
    public async Task Get_GivenAContribution_NamesTheSubjectAndFillsInOnlyThatAsksAnswer()
    {
        // arrange
        var exchange = await SeedAsync();
        var ask = await GammaAskedAboutBetaAsync(exchange);
        await ShareOutrightAsync(exchange.GammaId, "Gamma's own wish");
        await _provider.AddContributedGiftIdeaAsync(ask.AskId, "A stand mixer");

        // act
        var response = await _sut.FunctionHandler(Get(ask.Token), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("Gift ideas for Beta");
        response.Body.Should().Contain("They'll see the ideas");
        response.Body.Should().Contain(">A stand mixer</textarea>");
        response.Body.Should().NotContain("Gamma's own wish");
    }

    [Fact]
    public async Task Post_GivenAContributionNamingTheHelpersOwnPick_RefusesIt()
    {
        // arrange: Gamma drew Alpha, and Alpha is who this contribution would be sent to.
        var exchange = await SeedAsync();
        var ask = await GammaAskedAboutBetaAsync(exchange);

        // act
        var response = await _sut.FunctionHandler(
            Post(ask.Token, "Beta likes what Alpha likes."), new FakeLambdaContext());

        // assert: the check guards the writer's own pick rather than the subject, and on this path
        // the person it would leak to is the very person the message is going to.
        response.Body.Should().Contain("mentions the name of the person you picked");
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_GivenTheBoxTicked_StoresTheIdeasAndSendsNothing()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.Token, "A cast iron skillet.", holdUntilAsked: true), new FakeLambdaContext());

        // assert: the whole promise of the checkbox. Nobody has asked, so nobody hears about this.
        _sent.Should().BeEmpty();
        response.Body.Should().Contain("Saved!");
        response.Body.Should().Contain("only if they ask for gift ideas");

        await using var context = _contextFactory.CreateDbContext();

        var stored = await context.GiftIdeas
            .Where(giftIdea => giftIdea.ParticipantId == exchange.AlphaId)
            .ToListAsync();

        stored.Should().ContainSingle().Which.HoldUntilAsked.Should().BeTrue();
        stored.Single().Ideas.Should().Be("A cast iron skillet.");
    }

    [Fact]
    public async Task Post_GivenTheBoxTickedAndTheirGiverHasAlreadyAsked_SendsItStraightAway()
    {
        // arrange: Gamma drew Alpha and has already asked for ideas about them. Somebody who writes
        // after being asked is owed to somebody who is waiting.
        var exchange = await SeedAsync();
        await AskedAboutAlphaAsync(exchange);

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.Token, "A cast iron skillet.", holdUntilAsked: true), new FakeLambdaContext());

        // assert
        var forward = SentMessages().Should().ContainSingle().Subject;
        forward.To.Mailboxes.Single().Address.Should().Be(exchange.GammaEmail);
        forward.HtmlBody.Should().Contain("A cast iron skillet.");

        // Stamped, so the next round of asking does not send the same text again.
        (await ReleasedAtAsync(exchange)).Should().BeAfter(DateTimeOffset.MinValue);

        // And the page says nothing about it, which the test below pins exactly.
        response.Body.Should().NotContain("asked for");
    }

    [Fact]
    public async Task Post_GivenTheBoxTicked_SaysTheSameThingWhetherItWasHeldOrSent()
    {
        // arrange: two exchanges, identical but for whether the person holding Alpha's name has
        // asked. Told apart, this page would tell a participant that their giver has been asking
        // about them — which is the one thing asking somebody else instead of them is for.
        var held = await SeedAsync();
        var released = await SeedAsync();
        await AskedAboutAlphaAsync(released);

        // act
        var heldResponse = await _sut.FunctionHandler(
            Post(held.Token, "A cast iron skillet.", holdUntilAsked: true), new FakeLambdaContext());

        var releasedResponse = await _sut.FunctionHandler(
            Post(released.Token, "A cast iron skillet.", holdUntilAsked: true), new FakeLambdaContext());

        // assert: word for word, and one of the two did send an email.
        releasedResponse.Body.Should().Be(heldResponse.Body);
        _sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Post_GivenTheBoxTickedAndARefusedSubmission_HandsTheBoxBackTicked()
    {
        // arrange: Alpha drew Beta, so naming Beta is refused.
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Post(exchange.Token, "Something Beta would also like", holdUntilAsked: true),
            new FakeLambdaContext());

        // assert: a box handed back unticked would make the corrected retry an immediate send, which
        // is the one mistake on this page that cannot be taken back.
        response.Body.Should().Contain("mentions the name of the person you picked");
        response.Body.Should().Contain("value=\"yes\" checked");

        _sent.Should().BeEmpty();
        await ShouldHaveStoredNothing(exchange);
    }

    [Fact]
    public async Task Post_GivenAUrlEncodedBodyWithTheBoxTicked_ReadsItTheSame()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            PostUrlEncoded(exchange.Token, "A scarf.", holdUntilAsked: true), new FakeLambdaContext());

        // assert
        _sent.Should().BeEmpty();
        (await _provider.GetLatestGiftIdeaAsync(exchange.AlphaId)).HoldUntilAsked.Should().BeTrue();
    }

    [Fact]
    public async Task Get_GivenHeldIdeas_TicksTheBoxAndNeverMentionsAnAsk()
    {
        // arrange: held, and since released, because Gamma asked.
        var exchange = await SeedAsync();
        await HoldAsync(exchange.AlphaId, "A cast iron skillet");
        await AskedAboutAlphaAsync(exchange);

        // act
        var response = await _sut.FunctionHandler(Get(exchange.Token), new FakeLambdaContext());

        // assert: the standing choice is shown, and nothing about who has asked for what. A box that
        // changed on its own would be as good as telling them.
        response.Body.Should().Contain("value=\"yes\" checked");
        response.Body.Should().Contain("replaces it");
        response.Body.Should().NotContain("asked");
    }

    [Fact]
    public async Task Get_GivenTheyHaveSharedOutrightBefore_SaysThatCannotBeTakenBack()
    {
        // arrange
        var exchange = await SeedAsync();
        await ShareOutrightAsync(exchange.AlphaId, "A scarf");

        // act
        var response = await _sut.FunctionHandler(Get(exchange.Token), new FakeLambdaContext());

        // assert: "these will never be seen by anyone" is not true of an email already sent, and the
        // page must not imply that ticking the box now recalls it.
        response.Body.Should().Contain("can't take those back");
    }

    [Fact]
    public async Task Get_GivenAContribution_DoesNotOfferToHoldAnythingBack()
    {
        // arrange
        var exchange = await SeedAsync();
        var ask = await GammaAskedAboutBetaAsync(exchange);

        // act
        var response = await _sut.FunctionHandler(Get(ask.Token), new FakeLambdaContext());

        // assert: whoever is writing here was asked, so waiting for an ask would be waiting for
        // something that has already happened.
        response.Body.Should().NotContain(ShareIdeasPageComposer.HoldUntilAskedField);
        response.Body.Should().NotContain("Only share if");
    }

    [Fact]
    public async Task Post_GivenAContributionAskingToHoldItBack_IgnoresThat()
    {
        // arrange
        var exchange = await SeedAsync();
        var ask = await GammaAskedAboutBetaAsync(exchange);

        // act: the form never offers this, so a body carrying it was hand-made.
        await _sut.FunctionHandler(
            Post(ask.Token, "Beta has been after a stand mixer.", holdUntilAsked: true),
            new FakeLambdaContext());

        // assert: it goes to the person who asked, exactly as an answer to an ask does.
        var forward = SentMessages().Should().ContainSingle().Subject;
        forward.To.Mailboxes.Single().Address.Should().Be(exchange.AlphaEmail);
    }

    /// <summary>Gamma, who drew Alpha, asks for gift ideas about Alpha.</summary>
    private Task<RecordGiftIdeaEnquiryResponse> AskedAboutAlphaAsync(SeededExchange exchange) =>
        _provider.RecordGiftIdeaEnquiryAsync(new RecordGiftIdeaEnquiryRequest
        {
            AskerParticipantId = exchange.GammaId,
            SubjectParticipantId = exchange.AlphaId
        });

    private async Task<DateTimeOffset> ReleasedAtAsync(SeededExchange exchange)
    {
        await using var context = _contextFactory.CreateDbContext();

        return await context.GiftIdeaEnquiries
            .Where(enquiry => enquiry.AskerParticipantId == exchange.GammaId
                              && enquiry.SubjectParticipantId == exchange.AlphaId)
            .Select(enquiry => enquiry.ReleasedAt)
            .SingleAsync();
    }

    /// <summary>Alpha, who drew Beta, asks Gamma what Beta might like.</summary>
    private async Task<(Guid AskId, string Token)> GammaAskedAboutBetaAsync(SeededExchange exchange)
    {
        var token = await _provider.IssueGiftIdeaAskAsync(exchange.AlphaId, exchange.GammaId, exchange.BetaId);

        await using var context = _contextFactory.CreateDbContext();

        var askId = await context.GiftIdeaAsks
            .Where(ask => ask.TokenHash == SecretToken.Hash(token))
            .Select(ask => ask.GiftIdeaAskId)
            .SingleAsync();

        return (askId, token);
    }

    /// <summary>A submission already shared outright, which is what these arrangements mean.</summary>
    private Task<Guid> ShareOutrightAsync(Guid participantId, string ideas) =>
        _provider.AddGiftIdeaAsync(new AddGiftIdeaRequest
        {
            ParticipantId = participantId,
            Ideas = ideas,
            HoldUntilAsked = false
        });

    /// <summary>A submission written down and held back until somebody asks for it.</summary>
    private Task<Guid> HoldAsync(Guid participantId, string ideas) =>
        _provider.AddGiftIdeaAsync(new AddGiftIdeaRequest
        {
            ParticipantId = participantId,
            Ideas = ideas,
            HoldUntilAsked = true
        });

    private async Task ShouldHaveStoredNothing(SeededExchange exchange)
    {
        await using var context = _contextFactory.CreateDbContext();
        (await context.GiftIdeas.AnyAsync(row => row.ParticipantId == exchange.AlphaId)).Should().BeFalse();
    }

    /// <summary>
    /// A hat drawn in a three-way cycle — Alpha drew Beta, Beta drew Gamma, Gamma drew Alpha — with
    /// tokens issued. Three, so that who Alpha drew and who drew Alpha are different people.
    /// </summary>
    private async Task<SeededExchange> SeedAsync()
    {
        var hat = _hatFaker.Generate();
        await _provider.CreateHatAsync(hat);

        var alpha = await AddAsync(hat, "Alpha");
        var beta = await AddAsync(hat, "Beta");
        var gamma = await AddAsync(hat, "Gamma");

        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, alpha, beta);
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, beta, gamma);
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, gamma, alpha);
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
            alpha,
            ids[beta],
            ids[gamma],
            gamma,
            tokens[alpha]);
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

    private static APIGatewayProxyRequest Get(string token) =>
        new()
        {
            HttpMethod = "GET",
            Resource = "/ideas/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token }
        };

    /// <summary>
    /// What a browser submitting the share form sends: multipart, as the form declares.
    /// </summary>
    /// <remarks>
    /// An unticked checkbox is not posted at all, which is why holding back is expressed here by the
    /// part being there rather than by anything it contains.
    /// </remarks>
    private static APIGatewayProxyRequest Post(string token, string ideas, bool holdUntilAsked = false)
    {
        const string boundary = "----WebKitFormBoundaryx7Qp2ZcJ4mTn9aLk";

        var hold = holdUntilAsked
            ? $"--{boundary}\r\n"
              + $"Content-Disposition: form-data; name=\"{ShareIdeasPageComposer.HoldUntilAskedField}\"\r\n\r\n"
              + "yes\r\n"
            : string.Empty;

        return new APIGatewayProxyRequest
        {
            HttpMethod = "POST",
            Resource = "/ideas/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token },
            // Lower-cased, as HTTP/2 clients send it.
            Headers = new Dictionary<string, string> { ["content-type"] = $"multipart/form-data; boundary={boundary}" },
            Body = $"--{boundary}\r\n"
                   + $"Content-Disposition: form-data; name=\"{ShareIdeasPageComposer.IdeasField}\"\r\n\r\n"
                   + $"{ideas}\r\n"
                   + hold
                   + $"--{boundary}--\r\n"
        };
    }

    private static APIGatewayProxyRequest PostUrlEncoded(string token, string ideas, bool holdUntilAsked = false) =>
        new()
        {
            HttpMethod = "POST",
            Resource = "/ideas/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token },
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/x-www-form-urlencoded" },
            Body = $"{ShareIdeasPageComposer.IdeasField}={Uri.EscapeDataString(ideas)}"
                   + (holdUntilAsked ? $"&{ShareIdeasPageComposer.HoldUntilAskedField}=yes" : string.Empty)
        };

    private ImmutableList<MimeMessage> SentMessages() =>
        [.. _sent.Select(raw => MimeMessage.Load(new MemoryStream(raw)))];

    private sealed record SeededExchange(
        Guid HatId,
        string OrganizerEmail,
        Guid AlphaId,
        string AlphaEmail,
        Guid BetaId,
        Guid GammaId,
        string GammaEmail,
        string Token
    );
}
