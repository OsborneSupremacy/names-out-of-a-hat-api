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
/// The Ask, against a real database.
///
/// Two properties carry most of the weight here, and neither is visible from the happy path alone:
/// that a GET never sends anything, and that nothing reaching the person being asked names the
/// person who asked.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AskForGiftIdeasServiceTests
{
    static AskForGiftIdeasServiceTests()
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

    private readonly AskForGiftIdeasService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public AskForGiftIdeasServiceTests(PostgresFixture dbFixture)
    {
        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();

        _throttle.TryReserveAskSlotAsync(Arg.Any<ReserveAskSlotRequest>())
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

        _sut = new AskForGiftIdeasService(
            _provider,
            _throttle,
            new GiftIdeaEmailCompositionService(),
            new AskPageComposer(),
            new AutomaticEmailSender(_ses, Substitute.For<ILogger<AutomaticEmailSender>>()),
            new AskQuestionPolicy(),
            _moderation,
            Substitute.For<ILogger<AskForGiftIdeasService>>());
    }

    [Fact]
    public async Task Get_ListsEveryoneElseAndSendsNothing()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Request("GET", exchange.AlphaToken), new FakeLambdaContext());

        // assert: the whole reason this is two endpoints. A mail scanner following the link in an
        // invitation gets this page, and nobody is emailed on the participant's behalf.
        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("<form method=\"post\"");

        // Their own pick, and the one other person in the exchange. Not themselves.
        response.Body.Should().Contain(exchange.BetaId.ToString());
        response.Body.Should().Contain(exchange.GammaId.ToString());
        response.Body.Should().NotContain(exchange.AlphaId.ToString());

        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
        await _throttle.DidNotReceive()
            .TryReserveAskSlotAsync(Arg.Any<ReserveAskSlotRequest>());
    }

    [Fact]
    public async Task Get_TicksTheirOwnPickAndNobodyElse()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Request("GET", exchange.AlphaToken), new FakeLambdaContext());

        // assert: asking the person whose name you drew is the ordinary case, so submitting the
        // form untouched does that and nothing more.
        response.Body.Should().Contain($"value=\"{exchange.BetaId}\" checked");
        response.Body.Should().Contain($"value=\"{exchange.GammaId}\" style");
    }

    [Fact]
    public async Task Get_GivenASmallExchange_SaysTheAskerMayBeIdentified()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Request("GET", exchange.AlphaToken), new FakeLambdaContext());

        // assert: three people, so whoever Alpha asks knows it was not them and not Beta, which
        // leaves exactly Alpha. Saying so here is the only honest place — the asker is the one
        // person who can weigh it, and telling the helper would hand them the deduction.
        response.Body.Should().Contain("work out that it was you");
    }

    [Fact]
    public async Task Post_AsksThePersonTheParticipantDrew()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("We've asked Beta");

        var ask = SentMessages().Single();

        ask.To.Mailboxes.Single().Address.Should().Be(exchange.BetaEmail);
    }

    [Fact]
    public async Task Post_NeverNamesTheAsker()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert: the promise the button makes. In an exchange this small, naming the asker names
        // the one person holding this recipient's name.
        var ask = SentMessages().Single();

        ask.HtmlBody.Should().NotContain("Alpha");
        ask.HtmlBody.Should().NotContain(exchange.AlphaEmail);
        ask.Subject.Should().NotContain("Alpha");
        ask.HtmlBody.Should().Contain("Someone in");
    }

    [Fact]
    public async Task Post_GivesThemAWorkingLinkOfTheirOwn()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert: the point of the email. It carries the same SHARE GIFT IDEAS block the invitation
        // does, addressed to a token issued for them.
        var ask = SentMessages().Single();

        ask.HtmlBody.Should().Contain("SHARE GIFT IDEAS");
        ask.HtmlBody.Should().Contain("https://api.namesoutofahat.com/ideas/");

        await using var context = _contextFactory.CreateDbContext();

        var tokens = await context.GiftIdeaTokens
            .CountAsync(token => token.ParticipantId == exchange.BetaId);

        // Alongside the one they were issued with their invitation, not instead of it — theirs
        // cannot be reconstructed, and replacing it would kill the address already in their inbox.
        tokens.Should().Be(2);
    }

    [Fact]
    public async Task Post_LeavesTheOriginalAddressWorking()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert: the address in Beta's invitation still resolves to Beta.
        var (found, route) = await _provider.FindGiftIdeaRouteAsync(SecretToken.Hash(exchange.BetaToken));

        found.Should().BeTrue();
        route.ParticipantId.Should().Be(exchange.BetaId);
    }

    [Fact]
    public async Task Post_GivenTheThrottleRefuses_TellsTheAskerWhenTheyLastAsked()
    {
        // arrange
        var askedOn = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);

        _throttle.TryReserveAskSlotAsync(Arg.Any<ReserveAskSlotRequest>())
            .Returns(ReserveSlotResponses.RefusedSince(askedOn));

        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert: the person being asked hears nothing at all; the asker gets the date, because the
        // likeliest reader is somebody who does not remember asking.
        var message = SentMessages().Single();

        message.To.Mailboxes.Single().Address.Should().Be(exchange.AlphaEmail);
        message.HtmlBody.Should().Contain("3 August 2026");
        response.Body.Should().Contain("3 August 2026");
    }

    [Fact]
    public async Task Post_GivenTheThrottleRefuses_IssuesNoTokenAndAsksNobody()
    {
        // arrange
        _throttle.TryReserveAskSlotAsync(Arg.Any<ReserveAskSlotRequest>())
            .Returns(ReserveSlotResponses.RefusedSince(DateTimeOffset.UtcNow.AddDays(-2)));

        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert
        SentMessages().Should().AllSatisfy(message =>
            message.To.Mailboxes.Single().Address.Should().Be(exchange.AlphaEmail));

        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeaTokens.CountAsync(token => token.ParticipantId == exchange.BetaId))
            .Should().Be(1, "a refused Ask must not leave a token behind");
    }

    [Fact]
    public async Task Post_GivenAClosedExchange_SendsNothing()
    {
        // arrange
        var exchange = await SeedAsync();
        await _provider.UpdateHatStatusAsync(exchange.OrganizerEmail, exchange.HatId, HatStatus.Closed);

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("the link may have expired");
        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task GivenAnUnknownToken_SaysTheSameThingAsAFinishedExchange(string method)
    {
        // arrange
        await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Request(method, SecretToken.Create(), Guid.CreateVersion7()), new FakeLambdaContext());

        // assert: indistinguishable on purpose. Somebody holding a guessed token would otherwise
        // learn from the difference whether it named a real participant.
        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("the link may have expired");
        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_GivenSomebodyOtherThanTheirPick_AsksThemAboutThePick()
    {
        // arrange
        var exchange = await SeedAsync();

        // act: Alpha drew Beta, and would rather not tip Beta off, so asks Gamma instead.
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.GammaId), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("We've asked Gamma");

        var ask = SentMessages().Single();

        ask.To.Mailboxes.Single().Address.Should().Be(exchange.GammaEmail);

        // The subject of the ask is named, because a request for ideas that does not say who they
        // are for cannot be answered.
        ask.Subject.Should().Contain("Beta");
        ask.HtmlBody.Should().Contain("SHARE GIFT IDEAS FOR BETA");

        // The asker is not, which is the whole promise.
        ask.Subject.Should().NotContain("Alpha");
        ask.HtmlBody.Should().NotContain("Alpha");
        ask.HtmlBody.Should().NotContain(exchange.AlphaEmail);
    }

    [Fact]
    public async Task Post_GivenSomebodyOtherThanTheirPick_RecordsWhoAskedWhoAboutWhom()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.GammaId), new FakeLambdaContext());

        // assert: all three roles, because the reply has to be checked against Gamma, filed against
        // Beta and delivered to Alpha, and a token naming one participant could say none of that.
        await using var context = _contextFactory.CreateDbContext();

        var ask = await context.GiftIdeaAsks.SingleAsync(row => row.AskerParticipantId == exchange.AlphaId);

        ask.AskerParticipantId.Should().Be(exchange.AlphaId);
        ask.HelperParticipantId.Should().Be(exchange.GammaId);
        ask.SubjectParticipantId.Should().Be(exchange.BetaId);
    }

    [Fact]
    public async Task Post_GivenSomebodyOtherThanTheirPick_IssuesNoTokenAgainstThemselves()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.GammaId), new FakeLambdaContext());

        // assert: what Gamma is being asked for is ideas about Beta, not about Gamma. An extra
        // gift_idea_token here would file their answer as their own wishes and forward it to
        // whoever drew them.
        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeaTokens.CountAsync(token => token.ParticipantId == exchange.GammaId))
            .Should().Be(1, "only the token issued with their invitation");
    }

    [Fact]
    public async Task Post_AsksEverybodyChosen()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId, exchange.GammaId), new FakeLambdaContext());

        // assert: the point of the feature. One round, two different emails, one address each.
        var sent = SentMessages();

        sent.Select(message => message.To.Mailboxes.Single().Address)
            .Should().BeEquivalentTo([exchange.BetaEmail, exchange.GammaEmail]);

        response.Body.Should().Contain("Beta").And.Contain("Gamma");
    }

    [Fact]
    public async Task Post_GivenSomeAlreadyAsked_AsksTheRestAndWritesToTheAsker()
    {
        // arrange
        var askedOn = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);
        var exchange = await SeedAsync();

        _throttle.TryReserveAskSlotAsync(Arg.Is<ReserveAskSlotRequest>(ask => ask.TargetParticipantId == exchange.GammaId))
            .Returns(ReserveSlotResponses.RefusedSince(askedOn));

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId, exchange.GammaId), new FakeLambdaContext());

        // assert: one refusal does not cost the others. A per-asker limit would have sent nothing.
        var sent = SentMessages();

        sent.Should().HaveCount(2);
        sent.Should().Contain(message => message.To.Mailboxes.Single().Address == exchange.BetaEmail);

        // The second is the summary, to the asker, naming what did not happen and when.
        var summary = sent.Single(message => message.To.Mailboxes.Single().Address == exchange.AlphaEmail);

        summary.HtmlBody.Should().Contain("Gamma").And.Contain("3 August 2026");
        response.Body.Should().Contain("3 August 2026");
    }

    [Fact]
    public async Task Post_GivenEverythingWorked_WritesNoSummary()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId, exchange.GammaId), new FakeLambdaContext());

        // assert: the outcome is already on the page in front of them, so a round that went through
        // as asked produces no mail to the asker at all.
        SentMessages().Should().NotContain(message =>
            message.To.Mailboxes.Single().Address == exchange.AlphaEmail);
    }

    [Fact]
    public async Task Post_GivenNobodyChosen_ShowsTheListAgainAndSendsNothing()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken), new FakeLambdaContext());

        // assert: ticking nobody is a slip, and the useful answer to a slip is the form again.
        response.Body.Should().Contain("Choose at least one person to ask.");
        response.Body.Should().Contain("<form method=\"post\"");

        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_GivenTheirOwnId_AsksNobody()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.AlphaId), new FakeLambdaContext());

        // assert: the form never offers this, but a form is only ever a suggestion to a browser.
        response.Body.Should().Contain("Choose at least one person to ask.");

        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_GivenSomebodyFromAnotherExchange_AsksNobody()
    {
        // arrange
        var exchange = await SeedAsync();
        var stranger = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, stranger.GammaId), new FakeLambdaContext());

        // assert: an edited form must not become a way to mail somebody in an exchange the sender
        // has nothing to do with. Membership is checked against the database, not against the page
        // this application happened to render.
        response.Body.Should().Contain("Choose at least one person to ask.");

        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task GivenAnAskToken_IsNotItselfAnAskLink(string method)
    {
        // arrange: the address Gamma was given to answer Alpha's question about Beta.
        var exchange = await SeedAsync();

        var askToken = await _provider.IssueGiftIdeaAskAsync(
            exchange.AlphaId, exchange.GammaId, exchange.BetaId);

        // act
        var response = await _sut.FunctionHandler(
            Request(method, askToken, exchange.BetaId), new FakeLambdaContext());

        // assert: it writes back, and that is all it does. Treating it as an ask link would let
        // whoever holds it start rounds of their own from somebody else's arrangement, and the
        // subject of those rounds would be a pick that is not theirs.
        response.Body.Should().Contain("the link may have expired");

        await _ses.DidNotReceive().SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_IsNotCached()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Request("GET", exchange.AlphaToken), new FakeLambdaContext());

        // assert: a cached page would report an outcome that is no longer true.
        response.Headers["Cache-Control"].Should().Be("no-store");
        response.Headers["Content-Type"].Should().Be("text/html; charset=utf-8");
    }

    [Fact]
    public async Task Post_RecordsThatTheAskerAskedAboutTheirPick()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert: written down because Beta may write ideas to be held long after this, and the
        // share page has to be able to tell that somebody is waiting for them.
        await using var context = _contextFactory.CreateDbContext();

        var enquiry = await context.GiftIdeaEnquiries
            .Where(row => row.AskerParticipantId == exchange.AlphaId)
            .SingleAsync();

        enquiry.SubjectParticipantId.Should().Be(exchange.BetaId);
        enquiry.RequestedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Post_GivenTheirPickHasHeldIdeas_SendsThemToTheAsker()
    {
        // arrange: Beta wrote ideas down and asked us to wait until somebody wanted them.
        var exchange = await SeedAsync();
        await HoldAsync(exchange.BetaId, "A cast iron skillet");

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert
        var release = SentMessages()
            .Should().ContainSingle(message => Mentions(message, "A cast iron skillet"))
            .Subject;

        release.To.Mailboxes.Single().Address.Should().Be(exchange.AlphaEmail);
        release.Subject.Should().Be("Beta shared gift ideas with you");
        release.HtmlBody.Should().Contain("had already written down");

        // Said on the page too: the email would otherwise look like an answer that came back
        // impossibly fast.
        response.Body.Should().Contain("had already written down some gift ideas");

        // Beta is still asked, and told nothing about what was passed on.
        var ask = SentMessages().Single(message => message.To.Mailboxes.Single().Address == exchange.BetaEmail);

        ask.HtmlBody.Should().NotContain("A cast iron skillet");
    }

    [Fact]
    public async Task Post_GivenOnlyOtherPeopleAsked_StillSendsTheHeldIdeas()
    {
        // arrange: the case the feature exists for. Alpha would rather not tip Beta off, so they ask
        // Gamma instead — and Beta's own words are what Beta wanted them to have.
        var exchange = await SeedAsync();
        await HoldAsync(exchange.BetaId, "A cast iron skillet");

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.GammaId), new FakeLambdaContext());

        // assert
        SentMessages().Should().ContainSingle(message => Mentions(message, "A cast iron skillet"))
            .Which.To.Mailboxes.Single().Address.Should().Be(exchange.AlphaEmail);

        // Beta hears nothing at all. Being told would be being told that whoever holds their name has
        // been asking about them, which is what asking Gamma instead was for.
        SentMessages().Should().NotContain(message =>
            message.To.Mailboxes.Single().Address == exchange.BetaEmail);
    }

    [Fact]
    public async Task Post_AskingTwice_SendsTheHeldIdeasOnce()
    {
        // arrange
        var exchange = await SeedAsync();
        await HoldAsync(exchange.BetaId, "A cast iron skillet");

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.GammaId), new FakeLambdaContext());

        // assert: the second round finds the same text already passed on and leaves it alone.
        SentMessages().Where(message => Mentions(message, "A cast iron skillet"))
            .Should().HaveCount(1);
    }

    [Fact]
    public async Task Post_GivenEveryAskIsThrottled_StillSendsTheHeldIdeas()
    {
        // arrange: the throttle governs how often somebody may be mailed a question. It says nothing
        // about ideas already written down for whoever asked.
        _throttle.TryReserveAskSlotAsync(Arg.Any<ReserveAskSlotRequest>())
            .Returns(ReserveSlotResponses.RefusedSince(DateTimeOffset.UtcNow.AddDays(-2)));

        var exchange = await SeedAsync();
        await HoldAsync(exchange.BetaId, "A cast iron skillet");

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert
        SentMessages().Should().ContainSingle(message => Mentions(message, "A cast iron skillet"));

        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeaEnquiries.AnyAsync(row => row.AskerParticipantId == exchange.AlphaId))
            .Should().BeTrue("asking is what this records, not whether an email went out");
    }

    [Fact]
    public async Task Post_GivenTheirPickSharedOutright_SendsNothingBeyondTheAsk()
    {
        // arrange: Beta shared without holding anything back, so Alpha already has it. Asking must
        // not deliver it a second time.
        var exchange = await SeedAsync();
        await ShareOutrightAsync(exchange.BetaId, "A cast iron skillet");

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, exchange.BetaId), new FakeLambdaContext());

        // assert
        SentMessages().Should().ContainSingle()
            .Which.To.Mailboxes.Single().Address.Should().Be(exchange.BetaEmail);

        response.Body.Should().NotContain("had already written down");
    }

    [Fact]
    public async Task Post_GivenNobodyChosen_RecordsNothing()
    {
        // arrange
        var exchange = await SeedAsync();
        await HoldAsync(exchange.BetaId, "A cast iron skillet");

        // act: ticking nobody is a slip, and a slip is not an ask.
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("Choose at least one person to ask.");
        _sent.Should().BeEmpty();

        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeaEnquiries.AnyAsync(row => row.AskerParticipantId == exchange.AlphaId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Get_ShowsTheQuestionBoxAndTheIdentityWarning()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(Request("GET", exchange.AlphaToken), new FakeLambdaContext());

        // assert: the warning belongs beside the box, where somebody typing is looking.
        response.Body.Should().Contain($"name=\"{AskPageComposer.QuestionField}\"");
        response.Body.Should().Contain($"maxlength=\"{AskQuestionPolicy.MaxLength}\"");
        response.Body.Should().Contain("Be careful not to reveal your");
    }

    [Fact]
    public async Task Post_GivenAQuestion_QuotesItInEveryAsk()
    {
        // arrange
        var exchange = await SeedAsync();

        // act: one to their pick, one to somebody else about their pick.
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, "What shirt size do they wear?", exchange.BetaId, exchange.GammaId),
            new FakeLambdaContext());

        // assert
        var sent = SentMessages();
        sent.Should().HaveCount(2);
        sent.Should().OnlyContain(message => Mentions(message, "What shirt size do they wear?"));
        sent.Should().OnlyContain(message => Mentions(message, "They also asked:"));
        await _moderation.Received(1).ModerateAsync("What shirt size do they wear?", Arg.Any<string>());
    }

    [Fact]
    public async Task Post_GivenNoQuestion_SendsTheAskWithoutOne()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, "   ", exchange.BetaId, exchange.GammaId), new FakeLambdaContext());

        // assert: the box is optional, so leaving it blank is the ordinary case and costs nothing.
        var sent = SentMessages();
        sent.Should().HaveCount(2);
        sent.Should().NotContain(message => Mentions(message, "They also asked:"));
        await _moderation.DidNotReceive().ModerateAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Post_GivenAQuestionModerationRefuses_AsksNobodyAndKeepsTheForm()
    {
        // arrange
        var exchange = await SeedAsync();
        await HoldAsync(exchange.BetaId, "A cast iron skillet");
        _moderation.ModerateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ModerationVerdict.Toxic);

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, "Something unkind", exchange.GammaId), new FakeLambdaContext());

        // assert: nothing sent, no throttle slot spent, no held ideas released — and what they
        // typed and ticked is still there to fix.
        response.Body.Should().Contain(AskPageComposer.ExplainRefusal(AskQuestionOutcome.RejectedInappropriateContent));
        response.Body.Should().Contain("Something unkind</textarea>");
        response.Body.Should().Contain($"value=\"{exchange.GammaId}\" checked");
        response.Body.Should().Contain($"value=\"{exchange.BetaId}\" style");
        _sent.Should().BeEmpty();

        await _throttle.DidNotReceive().TryReserveAskSlotAsync(Arg.Any<ReserveAskSlotRequest>());

        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeaEnquiries.AnyAsync(row => row.AskerParticipantId == exchange.AlphaId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Post_GivenModerationIsUnavailable_AsksThemToTryAgainRatherThanReword()
    {
        // arrange
        var exchange = await SeedAsync();
        _moderation.ModerateAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ModerationVerdict.Unavailable);

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, "Do they like jazz?", exchange.BetaId), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain(AskPageComposer.ExplainRefusal(AskQuestionOutcome.RejectedModerationUnavailable));
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_GivenTheirOwnNameInTheQuestion_AsksNobody()
    {
        // arrange
        var exchange = await SeedAsync();

        // act: signing off is a habit, and the one habit that gives the whole thing away.
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, "Do they need gloves? Thanks, Alpha", exchange.BetaId),
            new FakeLambdaContext());

        // assert: refused on the rule, so moderation was never consulted.
        response.Body.Should().Contain(AskPageComposer.ExplainRefusal(AskQuestionOutcome.RejectedWouldRevealAsker));
        _sent.Should().BeEmpty();
        await _moderation.DidNotReceive().ModerateAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Post_GivenNobodyChosen_KeepsTheQuestion()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        var response = await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, "Favourite colour?"), new FakeLambdaContext());

        // assert
        response.Body.Should().Contain("Choose at least one person to ask.");
        response.Body.Should().Contain("Favourite colour?</textarea>");
    }

    [Fact]
    public async Task Post_EncodesTheQuestionInTheEmail()
    {
        // arrange
        var exchange = await SeedAsync();

        // act
        await _sut.FunctionHandler(
            Request("POST", exchange.AlphaToken, "<b>Size?</b>\nOr colour?", exchange.BetaId), new FakeLambdaContext());

        // assert: their words, shown as words, with their line break kept.
        var ask = SentMessages().Single();
        Mentions(ask, "<b>Size?</b>").Should().BeFalse();
        Mentions(ask, "&lt;b&gt;Size?&lt;/b&gt;<br />Or colour?").Should().BeTrue();
    }

    /// <summary>
    /// Whether a message's body carries this text. <c>HtmlBody</c> is nullable and every message
    /// these tests look at has one, so the check belongs here rather than in each assertion.
    /// </summary>
    private static bool Mentions(MimeMessage message, string text) =>
        message.HtmlBody?.Contains(text, StringComparison.Ordinal) == true;

    /// <summary>A submission Beta wrote and asked us to hold until somebody asks for it.</summary>
    private Task<Guid> HoldAsync(Guid participantId, string ideas) =>
        _provider.AddGiftIdeaAsync(new AddGiftIdeaRequest
        {
            ParticipantId = participantId,
            Ideas = ideas,
            HoldUntilAsked = true
        });

    /// <summary>A submission already sent to whoever drew them at the time it was written.</summary>
    private Task<Guid> ShareOutrightAsync(Guid participantId, string ideas) =>
        _provider.AddGiftIdeaAsync(new AddGiftIdeaRequest
        {
            ParticipantId = participantId,
            Ideas = ideas,
            HoldUntilAsked = false
        });

    /// <summary>
    /// A request to /ask/{token}, with the chosen participants posted as the unscripted form on the
    /// page posts them: one repeated field, url-encoded.
    /// </summary>
    private static APIGatewayProxyRequest Request(string method, string token, params Guid[] chosen) =>
        Request(method, token, null, chosen);

    /// <summary>The same, with the question box filled in.</summary>
    private static APIGatewayProxyRequest Request(string method, string token, string? question, params Guid[] chosen) =>
        new()
        {
            HttpMethod = method,
            Resource = "/ask/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token },
            Body = string.Join("&", chosen
                .Select(id => $"{AskPageComposer.ChoiceField}={id}")
                .Concat(question is null
                    ? []
                    : [$"{AskPageComposer.QuestionField}={HttpUtility.UrlEncode(question)}"]))
        };

    private ImmutableList<MimeMessage> SentMessages() =>
        [.. _sent.Select(raw => MimeMessage.Load(new MemoryStream(raw)))];

    /// <summary>Alpha drew Beta, Beta drew Gamma, Gamma drew Alpha, with tokens issued.</summary>
    /// <remarks>
    /// Three people, which is the smallest exchange that can run and the one where asking somebody
    /// other than your own pick gives the asker away completely. Kept deliberately: the tests that
    /// matter most here are about what is and is not said, and this is the shape that punishes a
    /// slip.
    /// </remarks>
    private async Task<SeededExchange> SeedAsync()
    {
        var hat = _hatFaker.Generate();
        await _provider.CreateHatAsync(hat);

        var alpha = await AddAsync(hat, "Alpha");
        var beta = await AddAsync(hat, "Beta");
        var gamma = await AddAsync(hat, "Gamma");

        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, alpha, "Beta");
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, beta, "Gamma");
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, gamma, "Alpha");
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
            tokens[alpha],
            ids[beta],
            beta,
            tokens[beta],
            ids[gamma],
            gamma);
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
        string AlphaEmail,
        string AlphaToken,
        Guid BetaId,
        string BetaEmail,
        string BetaToken,
        Guid GammaId,
        string GammaEmail
    );
}
