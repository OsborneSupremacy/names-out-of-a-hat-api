using System.Text.RegularExpressions;
using System.Web;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using GiftExchange.Library.Contexts;
using GiftExchange.Library.Entities;
using GiftExchange.Library.Utility;
using Microsoft.Extensions.Logging;
using MimeKit;
using NSubstitute;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The box at the bottom of a follow-up email for somebody whose invitation went unseen, and the copy
/// of the invitation it links to, against a real database.
///
/// The property that matters most runs one way: nobody who has not seen their invitation is ever
/// taken to have seen it. Showing the box once too often costs a few lines; hiding it from the one
/// person it is for costs them the exchange.
/// </summary>
[Collection(PostgresCollection.Name)]
public class InvitationReminderTests
{
    private const string BoxHeading = "First time hearing of this gift exchange?";

    static InvitationReminderTests()
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

    private readonly InvitationReminderService _reminder;

    private readonly AskForGiftIdeasService _ask;

    private readonly ShareGiftIdeasService _share;

    private readonly ViewInvitationService _view;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public InvitationReminderTests(PostgresFixture dbFixture)
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

        var sender = new AutomaticEmailSender(_ses, Substitute.For<ILogger<AutomaticEmailSender>>());

        _reminder = new InvitationReminderService(_provider, Substitute.For<ILogger<InvitationReminderService>>());

        _ask = new AskForGiftIdeasService(
            _provider,
            new GiftIdeaAsking(
                _provider,
                _throttle,
                new GiftIdeaEmailCompositionService(),
                sender,
                _reminder,
                new AskQuestionPolicy(),
                _moderation,
                Substitute.For<ILogger<GiftIdeaAsking>>()),
            new AskPageComposer());

        _share = new ShareGiftIdeasService(
            _provider,
            new GiftIdeaSharing(
                _provider,
                new GiftIdeaContentPolicy(),
                _moderation,
                new GiftIdeaEmailCompositionService(),
                sender,
                _reminder,
                Substitute.For<ILogger<GiftIdeaSharing>>()),
            new ShareIdeasPageComposer());

        _view = new ViewInvitationService(_provider, new EmailCompositionService());
    }

    [Fact]
    public async Task AnAsk_ToSomebodyWhoHasPressedNothing_EndsWithTheReminder()
    {
        // arrange
        var exchange = await SeedAsync();

        // act: Alpha asks Beta what they would like.
        await AskAsync(exchange.AlphaToken, exchange.BetaId);

        // assert
        var html = SentTo(exchange.BetaEmail).Single().HtmlBody;

        html.Should().Contain(BoxHeading);
        html.Should().Contain($"{Branding.InvitationUrl}/");
        html.Should().Contain(
            HttpUtility.HtmlEncode(EmailCompositionService.GetSubject(exchange.OrganizerName, exchange.HatName)),
            "the subject line is what finds the invitation in a search, whichever folder it is in");
        html.IndexOf(BoxHeading, StringComparison.Ordinal)
            .Should().BeGreaterThan(html.IndexOf("We won't tell you who asked", StringComparison.Ordinal),
                "the box follows the message rather than standing in front of it");
    }

    [Fact]
    public async Task PressingAButtonFromTheInvitation_HidesTheReminderFromThen_On()
    {
        // arrange: Beta shares ideas from the button in their invitation.
        var exchange = await SeedAsync();
        await _share.FunctionHandler(ShareRequest(exchange.BetaToken, "A good book"), new FakeLambdaContext());

        // act
        await AskAsync(exchange.AlphaToken, exchange.BetaId);

        // assert
        SentTo(exchange.BetaEmail).Single().HtmlBody.Should().NotContain(BoxHeading);
    }

    [Fact]
    public async Task PressingAButtonFromAnAskEmail_DoesNotHideTheReminder()
    {
        // arrange: the email carrying this token may be the first thing Beta ever read from us.
        var exchange = await SeedAsync();

        var askToken = await _provider.IssueGiftIdeaTokenAsync(new IssueGiftIdeaTokenRequest
        {
            ParticipantId = exchange.BetaId,
            ProvesInvitationSeen = false
        });

        await _share.FunctionHandler(ShareRequest(askToken, "A good book"), new FakeLambdaContext());

        // act
        var facts = await _provider.GetInvitationReminderAsync(exchange.BetaId);

        // assert
        facts.IsNeeded.Should().BeTrue();
    }

    [Fact]
    public async Task FetchingALink_IsNotPressingAButton()
    {
        // arrange: what a mail scanner does to every link in a delivered invitation.
        var exchange = await SeedAsync();
        await _share.FunctionHandler(ShareGet(exchange.BetaToken), new FakeLambdaContext());
        await _ask.FunctionHandler(AskRequest("GET", exchange.BetaToken), new FakeLambdaContext());

        // act
        var facts = await _provider.GetInvitationReminderAsync(exchange.BetaId);

        // assert
        facts.IsNeeded.Should().BeTrue();
    }

    [Fact]
    public async Task TheInvitationPage_ShowsTheirPickWithWorkingButtonsAndNoLeaveLink()
    {
        // arrange
        var exchange = await SeedAsync();
        await AskAsync(exchange.AlphaToken, exchange.BetaId);
        var pageToken = InvitationTokenIn(SentTo(exchange.BetaEmail).Single().HtmlBody);

        // act
        var response = await _view.FunctionHandler(ViewRequest(pageToken), new FakeLambdaContext());

        // assert
        response.StatusCode.Should().Be(200);
        response.Headers["Cache-Control"].Should().Be("no-store");
        response.Body.Should().Contain("Your invitation");
        response.Body.Should().Contain("Dear Beta,");
        response.Body.Should().Contain("Gamma", "Beta drew Gamma");
        response.Body.Should().Contain($"{Branding.ApiUrl}/ask/{HttpUtility.UrlEncode(pageToken)}");
        response.Body.Should().NotContain(Branding.LeaveUrl);
        Regex.Matches(response.Body, Regex.Escape(Branding.LogoUrl)).Count
            .Should().Be(1, "the page shell carries the wordmark, so the invitation inside it does not");
    }

    [Fact]
    public async Task PressingAButtonOnTheInvitationPage_HidesTheReminder()
    {
        // arrange: Beta found the invitation through the box, and shared from the page.
        var exchange = await SeedAsync();
        await AskAsync(exchange.AlphaToken, exchange.BetaId);
        var pageToken = InvitationTokenIn(SentTo(exchange.BetaEmail).Single().HtmlBody);

        await _share.FunctionHandler(ShareRequest(pageToken, "A good book"), new FakeLambdaContext());

        // act
        var facts = await _provider.GetInvitationReminderAsync(exchange.BetaId);

        // assert
        facts.IsNeeded.Should().BeFalse();
    }

    [Fact]
    public async Task TheInvitationPage_GivenAnExchangeDrawnAgain_IsUnavailable()
    {
        // arrange: a token outlives a new draw until invitations are sent again, and the pick it
        // would show is one nobody has been told yet.
        var exchange = await SeedAsync();
        await _provider.UpdateHatStatusAsync(exchange.OrganizerEmail, exchange.HatId, HatStatus.NamesAssigned);

        // act
        var response = await _view.FunctionHandler(ViewRequest(exchange.BetaToken), new FakeLambdaContext());

        // assert
        response.Body.Should().Be(ViewInvitationService.ComposeUnavailable());
    }

    [Fact]
    public async Task TheInvitationPage_GivenAnUnknownToken_IsUnavailable()
    {
        // act
        var response = await _view.FunctionHandler(ViewRequest("not-a-token"), new FakeLambdaContext());

        // assert
        response.Body.Should().Be(ViewInvitationService.ComposeUnavailable());
    }

    [Theory]
    [InlineData("NAMES_ASSIGNED")]
    [InlineData("CLOSED")]
    public async Task NoReminder_WhileNoInvitationStands(string status)
    {
        // arrange
        var exchange = await SeedAsync();
        await _provider.UpdateHatStatusAsync(exchange.OrganizerEmail, exchange.HatId, status);

        // act
        var facts = await _provider.GetInvitationReminderAsync(exchange.BetaId);

        // assert
        facts.IsNeeded.Should().BeFalse();
    }

    [Fact]
    public async Task TheOrganizer_IsNeverReminded()
    {
        // arrange: they sent the invitations.
        var exchange = await SeedAsync(organizerTakesPart: true);

        // act
        var facts = await _provider.GetInvitationReminderAsync(exchange.OrganizerParticipantId);

        // assert
        facts.IsNeeded.Should().BeFalse();
    }

    [Fact]
    public async Task TheDate_IsWhenTheirOwnInvitationWasSent()
    {
        // arrange: an address correction sends a second invitation later than the rest.
        var exchange = await SeedAsync();
        var resentAt = new DateTimeOffset(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);

        await using (var context = _contextFactory.CreateDbContext())
        {
            context.ParticipantEmailDeliveries.Add(new ParticipantEmailDeliveryEntity
            {
                ParticipantEmailDeliveryId = Guid.CreateVersion7(),
                ParticipantId = exchange.BetaId,
                MessageType = EmailMessageType.Invitation,
                SesMessageId = Guid.NewGuid().ToString(),
                Status = "DELIVERED",
                Detail = string.Empty,
                OccurredAt = resentAt,
                UpdatedAt = resentAt
            });
            await context.SaveChangesAsync();
        }

        // act
        var facts = await _provider.GetInvitationReminderAsync(exchange.BetaId);

        // assert
        facts.SentAt.Should().Be(resentAt);
        InvitationReminderService.Compose(facts, "token").Should().Contain("around November 27");
    }

    [Fact]
    public async Task TheReminder_IssuesATokenOnlyWhenItIsShown()
    {
        // arrange: Beta has already used their invitation.
        var exchange = await SeedAsync();
        await _share.FunctionHandler(ShareRequest(exchange.BetaToken, "A good book"), new FakeLambdaContext());

        await using var context = _contextFactory.CreateDbContext();
        var before = await context.GiftIdeaTokens.CountAsync(token => token.ParticipantId == exchange.BetaId);

        // act
        var box = await _reminder.ComposeForAsync(exchange.BetaId);

        // assert
        box.Should().BeEmpty();
        (await context.GiftIdeaTokens.CountAsync(token => token.ParticipantId == exchange.BetaId))
            .Should().Be(before, "a live link nobody was sent is a credential with no purpose");
    }

    private Task AskAsync(string askerToken, Guid chosen) =>
        _ask.FunctionHandler(AskRequest("POST", askerToken, chosen), new FakeLambdaContext());

    private static string InvitationTokenIn(string html)
    {
        var match = Regex.Match(html, Regex.Escape($"{Branding.InvitationUrl}/") + "([^\"]+)\"");
        match.Success.Should().BeTrue("the reminder links to the invitation page");
        return HttpUtility.UrlDecode(HttpUtility.HtmlDecode(match.Groups[1].Value));
    }

    private static APIGatewayProxyRequest AskRequest(string method, string token, params Guid[] chosen) =>
        new()
        {
            HttpMethod = method,
            Resource = "/ask/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token },
            Body = string.Join("&", chosen.Select(id => $"{AskPageComposer.ChoiceField}={id}"))
        };

    private static APIGatewayProxyRequest ShareGet(string token) =>
        new()
        {
            HttpMethod = "GET",
            Resource = "/ideas/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token }
        };

    private static APIGatewayProxyRequest ShareRequest(string token, string ideas) =>
        new()
        {
            HttpMethod = "POST",
            Resource = "/ideas/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token },
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/x-www-form-urlencoded" },
            Body = $"{ShareIdeasPageComposer.IdeasField}={Uri.EscapeDataString(ideas)}"
        };

    private static APIGatewayProxyRequest ViewRequest(string token) =>
        new()
        {
            HttpMethod = "GET",
            Resource = "/invitation/{token}",
            PathParameters = new Dictionary<string, string> { ["token"] = token }
        };

    private ImmutableList<MimeMessage> SentTo(string email) =>
    [
        .. _sent
            .Select(raw => MimeMessage.Load(new MemoryStream(raw)))
            .Where(message => message.To.Mailboxes.Any(mailbox =>
                mailbox.Address.Equals(email, StringComparison.OrdinalIgnoreCase)))
    ];

    /// <summary>Alpha drew Beta, Beta drew Gamma, Gamma drew Alpha, with invitations sent.</summary>
    private async Task<SeededExchange> SeedAsync(bool organizerTakesPart = false)
    {
        var hat = _hatFaker.Generate();
        await _provider.CreateHatAsync(hat);

        var alpha = await AddAsync(hat, "Alpha");
        var beta = await AddAsync(hat, "Beta");
        var gamma = await AddAsync(hat, "Gamma");

        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, alpha, beta);
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, beta, gamma);
        await _provider.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, gamma, alpha);

        if (organizerTakesPart)
            await _provider.CreateParticipantAsync(
                _participantFaker.Generate() with
                {
                    HatId = hat.HatId,
                    OrganizerEmail = hat.OrganizerEmail,
                    Email = hat.OrganizerEmail,
                    Name = "Organizer"
                },
                []);

        await _provider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.InvitationsSent);

        var tokens = await _provider.IssueGiftIdeaTokensAsync(hat.HatId);
        var ids = await _provider.GetParticipantIdsByEmailAsync(hat.HatId);
        var (_, stored) = await _provider.GetHatAsync(hat.OrganizerEmail, hat.HatId);

        return new SeededExchange(
            hat.HatId,
            hat.OrganizerEmail,
            stored.Organizer.Name,
            stored.Name,
            ids.GetValueOrDefault(hat.OrganizerEmail, Guid.Empty),
            tokens[alpha],
            ids[beta],
            beta,
            tokens[beta]);
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
        string OrganizerName,
        string HatName,
        Guid OrganizerParticipantId,
        string AlphaToken,
        Guid BetaId,
        string BetaEmail,
        string BetaToken
    );
}
