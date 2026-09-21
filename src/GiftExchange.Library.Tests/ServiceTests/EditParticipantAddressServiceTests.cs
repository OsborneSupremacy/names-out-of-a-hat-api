using GiftExchange.Library.Contexts;
using GiftExchange.Library.Entities;
using GiftExchange.Library.Utility;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// Correcting the address a participant was invited at, against a real database.
///
/// The behaviour worth pinning down is not that the column changes. It is what survives the change:
/// the participant row, and so their pick, their eligibility and everything SES has said about
/// them. Removing and re-adding somebody — the only way an organizer could do this before — takes
/// all of that with it, and after the hat is shaken that silently breaks the draw.
///
/// The provider is the real one, for the reason ShareGiftIdeasServiceTests gives. The queue and
/// the throttle are substitutes, because what matters about them is what they were handed.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EditParticipantAddressServiceTests
{
    static EditParticipantAddressServiceTests()
    {
        DotEnv.Load();
        Environment.SetEnvironmentVariable("LIVE_MODE", "true");
    }

    private readonly IEmailQueue _queue = Substitute.For<IEmailQueue>();

    private readonly IReplyThrottleProvider _throttle = Substitute.For<IReplyThrottleProvider>();

    private readonly List<GiftExchangeEmailRequest> _queued = [];

    private readonly GiftExchangeProvider _provider;

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly EditParticipantAddressService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public EditParticipantAddressServiceTests(PostgresFixture dbFixture)
    {
        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();

        _throttle.TryReserveAddressChangeSlotAsync(Arg.Any<ReserveAddressChangeSlotRequest>())
            .Returns(ReserveSlotResponses.Reserved);

        _queue.EnqueueAsync(Arg.Do<GiftExchangeEmailRequest>(email => _queued.Add(email)))
            .Returns(Task.CompletedTask);

        _sut = new EditParticipantAddressService(
            Substitute.For<ILogger<EditParticipantAddressService>>(),
            serviceProvider.GetRequiredService<ApiGatewayAdapter>(),
            new HatPreconditionValidator(
                Substitute.For<ILogger<HatPreconditionValidator>>(),
                _provider,
                new FakeContentModerationService()),
            _provider,
            new EmailCompositionService(),
            new CompletionEmailCompositionService(),
            _queue,
            _throttle,
            new DoNotAddService(_provider),
            serviceProvider.GetRequiredService<OrganizerStandingChecker>());
    }

    [Fact]
    public async Task BeforeInvitationsGoOut_TheAddressChangesAndNobodyIsEmailed()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.NamesAssigned);

        // act
        var result = await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert: nothing has been sent to anybody, so there is nothing to resend.
        result.IsFaulted.Should().BeFalse();
        result.Value.EmailResent.Should().BeFalse();
        result.Value.MessageType.Should().BeEmpty();
        _queued.Should().BeEmpty();

        await AddressShouldBeAsync(exchange, "fixed@example.com");
    }

    [Fact]
    public async Task AfterInvitationsGoOut_TheInvitationIsResentToTheNewAddress()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        var result = await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert
        result.IsFaulted.Should().BeFalse();
        result.Value.EmailResent.Should().BeTrue();
        result.Value.MessageType.Should().Be(EmailMessageType.Invitation);

        var sent = _queued.Should().ContainSingle().Subject;
        sent.RecipientEmail.Should().Be("fixed@example.com");
        sent.MessageType.Should().Be(EmailMessageType.Invitation);
        sent.ParticipantId.Should().Be(exchange.TargetParticipantId, "the delivery events have to land on the same row");
        sent.HtmlBody.Should().Contain(exchange.TargetPickedName, "it is still their invitation, so it still names their pick");
    }

    /// <summary>
    /// The address correction is the first thing that would break if a resend leaned on the token
    /// already sitting in somebody's mailbox. Only the hash of that one is kept, so it cannot be
    /// put into a new message; a fresh one is issued alongside it.
    /// </summary>
    [Fact]
    public async Task TheResentInvitation_CarriesAWorkingGiftIdeasAddress()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert
        await using var context = await _contextFactory.CreateDbContextAsync();

        var tokens = await context.GiftIdeaTokens
            .AsNoTracking()
            .Where(token => token.ParticipantId == exchange.TargetParticipantId)
            .ToListAsync();

        tokens.Should().NotBeEmpty();
        _queued.Single().HtmlBody.Should().Contain("https://api.namesoutofahat.com/ideas/");
    }

    /// <summary>
    /// A gift ideas link is the whole credential, and the wrong address was sent some. Correcting
    /// the address has to close every one of them off, or whoever reads that inbox keeps writing
    /// ideas in this participant's name.
    /// </summary>
    [Fact]
    public async Task TheOldAddress_NoLongerAuthorisesAnythingItWasSent()
    {
        // arrange: their own link, and an ask that reached them about somebody else.
        var exchange = await SeedAsync(HatStatus.InvitationsSent);
        var ids = await _provider.GetParticipantIdsByEmailAsync(exchange.HatId);

        var oldToken = await _provider.IssueGiftIdeaTokenAsync(exchange.TargetParticipantId);
        var oldAskToken = await _provider.IssueGiftIdeaAskAsync(
            ids[exchange.OtherEmail], exchange.TargetParticipantId, ids[exchange.OtherEmail]);

        var askId = await AskIdForAsync(oldAskToken);
        await _provider.AddContributedGiftIdeaAsync(askId, "A good umbrella");

        // act
        await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert
        (await _provider.FindGiftIdeaRouteAsync(SecretToken.Hash(oldToken))).found
            .Should().BeFalse("the link in the misdirected invitation must stop working");

        (await _provider.FindGiftIdeaContributionRouteAsync(SecretToken.Hash(oldAskToken))).found
            .Should().BeFalse("nor may the link in a misdirected ask");

        // The ask itself survives, because what was already shared on it records who it was about.
        (await _provider.GetLatestContributedGiftIdeaAsync(askId)).Should().Be("A good umbrella");

        // And the resent invitation carries a link that does work.
        await using var context = await _contextFactory.CreateDbContextAsync();

        (await context.GiftIdeaTokens.CountAsync(token => token.ParticipantId == exchange.TargetParticipantId))
            .Should().Be(1, "the old one was revoked and a fresh one issued for the resend");
    }

    /// <summary>
    /// A correction closes off credentials, and an enquiry is not one.
    /// </summary>
    /// <remarks>
    /// Nothing can be reached with it: it names two participants and authorises nobody to write
    /// anything. What it does say is that somebody asked for ideas about their pick, and that is still
    /// true after their address is fixed — the person who asked is the same person, at the address
    /// they actually read. Deleting it would only mean the same held submission being sent again the
    /// next time they asked. What already went to the wrong inbox cannot be recalled by any of this.
    /// </remarks>
    [Fact]
    public async Task TheOldAddress_DoesNotTakeWhatSomebodyAskedForWithIt()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);
        var ids = await _provider.GetParticipantIdsByEmailAsync(exchange.HatId);

        await _provider.RecordGiftIdeaEnquiryAsync(new RecordGiftIdeaEnquiryRequest
        {
            AskerParticipantId = ids[exchange.OtherEmail],
            SubjectParticipantId = exchange.TargetParticipantId
        });

        // act
        await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert
        await using var context = await _contextFactory.CreateDbContextAsync();

        (await context.GiftIdeaEnquiries.AnyAsync(row =>
                row.SubjectParticipantId == exchange.TargetParticipantId))
            .Should().BeTrue();
    }

    private async Task<Guid> AskIdForAsync(string token)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.GiftIdeaAsks
            .Where(ask => ask.TokenHash == SecretToken.Hash(token))
            .Select(ask => ask.GiftIdeaAskId)
            .SingleAsync();
    }

    /// <summary>
    /// The property that makes this an edit rather than a remove and re-add. Everything hanging off
    /// the participant is keyed by its id, so keeping the row keeps the draw intact.
    /// </summary>
    [Fact]
    public async Task TheParticipantRowSurvives_SoThePickAndTheDeliveryHistoryDo()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        await _provider.RecordDeliveryEventAsync(new ParticipantEmailDelivery
        {
            ParticipantId = exchange.TargetParticipantId,
            MessageType = EmailMessageType.Invitation,
            SesMessageId = "0100019-old-bounce",
            Status = DeliveryStatus.Bounced,
            Detail = "Permanent/General: smtp; 550 5.1.1 user unknown",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        // act
        await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert
        var ids = await _provider.GetParticipantIdsByEmailAsync(exchange.HatId);
        ids["fixed@example.com"].Should().Be(exchange.TargetParticipantId, "the row moved address, it was not replaced");

        await using var context = await _contextFactory.CreateDbContextAsync();

        (await context.ParticipantEmailDeliveries.CountAsync(row => row.ParticipantId == exchange.TargetParticipantId))
            .Should().Be(1, "what SES said about them is still about them");

        var (_, hat) = await _provider.GetHatAsync(exchange.OrganizerEmail, exchange.HatId);

        hat.Participants
            .Single(participant => participant.Person.Email == "fixed@example.com")
            .PickedRecipient.Should().Be(exchange.TargetPickedName, "the draw is untouched");
    }

    [Fact]
    public async Task AfterTheExchangeIsRevealed_TheAnnouncementIsResentRatherThanTheInvitation()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.Closed);

        // act
        var result = await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert: the invitation asks the reader to keep a secret everybody has now been told.
        result.Value.MessageType.Should().Be(EmailMessageType.Completion);
        _queued.Should().ContainSingle().Which.MessageType.Should().Be(EmailMessageType.Completion);
    }

    [Fact]
    public async Task AnAddressAlreadyInTheExchange_IsRefusedAndNothingIsSent()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        var result = await _sut.EditParticipantAddressAsync(Request(exchange, exchange.OtherEmail));

        // assert
        result.IsFaulted.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _queued.Should().BeEmpty();

        await AddressShouldBeAsync(exchange, exchange.TargetEmail);
    }

    [Fact]
    public async Task AnAddressNobodyInTheExchangeHas_IsNotFound()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        // act
        var result = await _sut.EditParticipantAddressAsync(
            Request(exchange, "fixed@example.com") with { CurrentEmail = "stranger@example.com" });

        // assert
        result.IsFaulted.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _queued.Should().BeEmpty();
    }

    /// <summary>
    /// The slot is claimed before anything is written, so a refusal leaves the exchange exactly as
    /// it was rather than moving the address and declining to say so.
    /// </summary>
    [Fact]
    public async Task WhenTheThrottleRefuses_NothingChangesAndNothingIsSent()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);

        _throttle.TryReserveAddressChangeSlotAsync(Arg.Any<ReserveAddressChangeSlotRequest>())
            .Returns(ReserveSlotResponses.RefusedSince(DateTimeOffset.UtcNow.AddMinutes(-2)));

        // act
        var result = await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert
        result.IsFaulted.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        _queued.Should().BeEmpty();

        await AddressShouldBeAsync(exchange, exchange.TargetEmail);
    }

    /// <summary>
    /// A resend is a send, and without the check a suspended organizer could reach a new stranger
    /// with every correction.
    /// </summary>
    [Fact]
    public async Task WhenTheOrganizerIsSuspended_NothingChangesAndNothingIsSent()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.InvitationsSent);
        await SuspendAsync(exchange.OrganizerEmail);

        // act
        var result = await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert
        result.IsFaulted.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _queued.Should().BeEmpty();

        await AddressShouldBeAsync(exchange, exchange.TargetEmail);
        await _throttle.DidNotReceive()
            .TryReserveAddressChangeSlotAsync(Arg.Any<ReserveAddressChangeSlotRequest>());
    }

    /// <summary>
    /// Nothing is sent before invitations go out, so a suspended organizer can still fix a typo.
    /// </summary>
    [Fact]
    public async Task WhenTheOrganizerIsSuspended_AnAddressCanStillBeFixedBeforeAnythingIsSent()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.NamesAssigned);
        await SuspendAsync(exchange.OrganizerEmail);

        // act
        var result = await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert
        result.IsFaulted.Should().BeFalse();
        await AddressShouldBeAsync(exchange, "fixed@example.com");
    }

    private async Task SuspendAsync(string organizerEmail)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.OrganizerComplaints.AddRange(Enumerable
            .Range(0, OrganizerStandingChecker.SuspendAtComplaints)
            .Select(_ => new OrganizerComplaintEntity
            {
                OrganizerComplaintId = Guid.CreateVersion7(),
                OrganizerEmailNormalized = organizerEmail.Trim().ToLowerInvariant(),
                EmailNormalized = $"{Guid.NewGuid():N}@example.com",
                ComplainedAt = DateTimeOffset.UtcNow.AddDays(-1)
            }));

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// A correction made before anything was sent mails nobody, so there is nothing to rate limit
    /// and no reason to make an organizer tidying up a draft wait.
    /// </summary>
    [Fact]
    public async Task BeforeInvitationsGoOut_TheThrottleIsNotConsulted()
    {
        // arrange
        var exchange = await SeedAsync(HatStatus.NamesAssigned);

        // act
        await _sut.EditParticipantAddressAsync(Request(exchange, "fixed@example.com"));

        // assert
        await _throttle.DidNotReceive()
            .TryReserveAddressChangeSlotAsync(Arg.Any<ReserveAddressChangeSlotRequest>());
    }

    private EditParticipantAddressRequest Request(Exchange exchange, string newEmail) =>
        new()
        {
            OrganizerEmail = exchange.OrganizerEmail,
            HatId = exchange.HatId,
            CurrentEmail = exchange.TargetEmail,
            NewEmail = newEmail
        };

    private async Task AddressShouldBeAsync(Exchange exchange, string expected)
    {
        var ids = await _provider.GetParticipantIdsByEmailAsync(exchange.HatId);

        ids.Should().ContainKey(expected);
        ids[expected].Should().Be(exchange.TargetParticipantId);
    }

    /// <summary>Three participants in a ring, so every pick resolves to a real name.</summary>
    private async Task<Exchange> SeedAsync(string status)
    {
        var hat = _hatFaker.Generate();
        await _provider.CreateHatAsync(hat);

        var created = new List<Participant>();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            var request = _participantFaker.Generate() with
            {
                HatId = hat.HatId,
                OrganizerEmail = hat.OrganizerEmail
            };

            created.Add(await _provider.CreateParticipantAsync(request, [.. created]));
        }

        for (var index = 0; index < created.Count; index++)
            await _provider.UpdateParticipantPickedRecipientAsync(
                hat.OrganizerEmail,
                hat.HatId,
                created[index].Person.Email,
                created[(index + 1) % created.Count].Person.Name);

        await _provider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, status);

        var ids = await _provider.GetParticipantIdsByEmailAsync(hat.HatId);

        return new Exchange
        {
            HatId = hat.HatId,
            OrganizerEmail = hat.OrganizerEmail,
            TargetEmail = created[0].Person.Email,
            TargetParticipantId = ids[created[0].Person.Email],
            TargetPickedName = created[1].Person.Name,
            OtherEmail = created[1].Person.Email
        };
    }

    private sealed record Exchange
    {
        public required Guid HatId { get; init; }
        public required string OrganizerEmail { get; init; }
        public required string TargetEmail { get; init; }
        public required Guid TargetParticipantId { get; init; }
        public required string TargetPickedName { get; init; }
        public required string OtherEmail { get; init; }
    }
}
