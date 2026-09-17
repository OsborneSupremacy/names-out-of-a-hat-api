using Amazon.Lambda.SQSEvents;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using GiftExchange.Library.Contexts;
using Microsoft.Extensions.Logging;
using MimeKit;
using NSubstitute;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The delayed check that tells an organizer which of their invitations came back.
///
/// Driven end to end against a real database, and through the real delivery event path rather than
/// by writing rows directly. What is worth pinning down is not that a filter works but that an SES
/// bounce, arriving on the queue the way one really does, ends up named in an email hours later —
/// and that everything which is not a bounce does not.
///
/// The last part is the reason this file is longer than the service. The costly mistake here is not
/// missing a failure; it is inventing one, because an organizer told somebody did not get their
/// invitation goes and pesters a person who is holding it.
/// </summary>
[Collection(PostgresCollection.Name)]
public class UndeliverableInvitationsServiceTests
{
    static UndeliverableInvitationsServiceTests()
    {
        // Static, because AutomaticEmailSender reads LIVE_MODE in its own constructor and the
        // substitutes below are field initialisers, which run first.
        DotEnv.Load();
        Environment.SetEnvironmentVariable("LIVE_MODE", "true");
    }

    private readonly IAmazonSimpleEmailService _ses = Substitute.For<IAmazonSimpleEmailService>();

    /// <summary>
    /// Raw MIME captured as each send happens, for the reason ShareGiftIdeasServiceTests gives:
    /// the sender disposes the buffer it wrote, so there is nothing to read off the recorded
    /// argument afterwards.
    /// </summary>
    private readonly List<byte[]> _sent = [];

    private readonly GiftExchangeProvider _provider;

    private readonly DeliveryEventsService _deliveryEvents;

    private readonly UndeliverableInvitationsService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public UndeliverableInvitationsServiceTests(PostgresFixture dbFixture)
    {
        var contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _deliveryEvents = serviceProvider.GetRequiredService<DeliveryEventsService>();

        _ses.When(ses => ses.SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var buffer = new MemoryStream();
                var data = ((SendRawEmailRequest)call[0]).RawMessage.Data;
                data.Position = 0;
                data.CopyTo(buffer);
                _sent.Add(buffer.ToArray());
            });

        _sut = new UndeliverableInvitationsService(
            _provider,
            new UndeliverableInvitationsEmailCompositionService(),
            new AutomaticEmailSender(_ses, Substitute.For<ILogger<AutomaticEmailSender>>()),
            Substitute.For<ILogger<UndeliverableInvitationsService>>());
    }

    [Fact]
    public async Task ABouncedInvitation_IsReportedToTheOrganizerWithTheAddressAndTheReason()
    {
        // arrange
        var exchange = await SeedAsync();

        await BounceAsync(exchange.BadId, "smtp; 550 5.1.1 user unknown");

        // act
        var sent = await _sut.ExecuteAsync(Schedule(exchange));

        // assert
        sent.Should().BeTrue();

        var notice = SentMessages().Should().ContainSingle().Subject;
        notice.To.Mailboxes.Single().Address.Should().Be(exchange.OrganizerEmail);

        // The three things that make this actionable: who, at what address, and what the far end
        // actually said. The last is the only part that names the real problem.
        notice.HtmlBody.Should().Contain(exchange.BadName);
        notice.HtmlBody.Should().Contain(exchange.BadEmail);
        notice.HtmlBody.Should().Contain("550 5.1.1 user unknown");

        // And where to go about it. Correcting an address is its own operation, and the one that
        // does not throw the draw away.
        notice.HtmlBody.Should().Contain($"/gift-exchange/{exchange.HatId}");
    }

    /// <summary>
    /// The distinction the whole delivery feature turns on, applied to an email this time. Nothing
    /// heard is not "did not arrive", and an organizer sent to chase somebody holding their
    /// invitation is worse off than one who was never written to.
    /// </summary>
    [Fact]
    public async Task ParticipantsNothingHasBeenHeardAbout_AreNotNamedAsFailures()
    {
        // arrange: one real bounce, and one participant with no events at all.
        var exchange = await SeedAsync();

        await BounceAsync(exchange.BadId, "smtp; 550 5.1.1 user unknown");

        // act
        await _sut.ExecuteAsync(Schedule(exchange));

        // assert
        var notice = SentMessages().Single();

        notice.HtmlBody.Should().NotContain(exchange.QuietEmail);
        notice.HtmlBody.Should().NotContain(exchange.QuietName);
    }

    [Fact]
    public async Task WhenEveryInvitationIsFine_NothingIsSent()
    {
        // arrange
        var exchange = await SeedAsync();

        await DeliverAsync(exchange.BadId);

        // act
        var sent = await _sut.ExecuteAsync(Schedule(exchange));

        // assert: an email saying nothing went wrong is one more thing to open and dismiss, and
        // the fastest way to teach an organizer to ignore the one that matters.
        sent.Should().BeFalse();
        _sent.Should().BeEmpty();
    }

    /// <summary>
    /// A complaint means the message arrived and was unwelcome. That is not a broken address and
    /// not something retyping one fixes, so it is deliberately not on this list.
    /// </summary>
    [Fact]
    public async Task AComplaint_IsNotTreatedAsAFailedDelivery()
    {
        // arrange
        var exchange = await SeedAsync();

        await ComplainAsync(exchange.BadId);

        // act
        var sent = await _sut.ExecuteAsync(Schedule(exchange));

        // assert
        sent.Should().BeFalse();
        _sent.Should().BeEmpty();
    }

    /// <summary>
    /// SES is still retrying while a message is delayed, so the outcome is not yet known. A notice
    /// sent about one would often be wrong by the time it was read.
    /// </summary>
    [Fact]
    public async Task AMessageStillBeingRetried_IsNotReportedYet()
    {
        // arrange
        var exchange = await SeedAsync();

        await DelayAsync(exchange.BadId);

        // act
        var sent = await _sut.ExecuteAsync(Schedule(exchange));

        // assert
        sent.Should().BeFalse();
        _sent.Should().BeEmpty();
    }

    /// <summary>
    /// The remedy for a bad address resends to that person by itself, which produces newer events.
    /// A notice still naming them would send an organizer to fix something already fixed.
    /// </summary>
    [Fact]
    public async Task AnAddressThatBouncedAndThenDelivered_IsNoLongerReported()
    {
        // arrange
        var exchange = await SeedAsync();

        await BounceAsync(exchange.BadId, "smtp; 550 5.1.1 user unknown");
        await DeliverAsync(exchange.BadId, occurredAt: "2026-08-28T12:00:00.000Z");

        // act
        var sent = await _sut.ExecuteAsync(Schedule(exchange));

        // assert
        sent.Should().BeFalse();
        _sent.Should().BeEmpty();
    }

    /// <summary>
    /// An exchange sends more than one thing to the same person. A bounced announcement against a
    /// finished exchange is not a failed invitation, and reporting it as one weeks later would be a
    /// claim about a message that arrived perfectly well at the time.
    /// </summary>
    [Fact]
    public async Task ABouncedMessageOfSomeOtherKind_IsNotReportedAsAFailedInvitation()
    {
        // arrange
        var exchange = await SeedAsync();

        await BounceAsync(exchange.BadId, "smtp; 550 5.1.1 user unknown", EmailMessageType.Completion);

        // act
        var sent = await _sut.ExecuteAsync(Schedule(exchange));

        // assert
        sent.Should().BeFalse();
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AClosedExchange_IsLeftAlone()
    {
        // arrange: the gifts have changed hands. There is nothing left to correct.
        var exchange = await SeedAsync();

        await BounceAsync(exchange.BadId, "smtp; 550 5.1.1 user unknown");
        await _provider.UpdateHatStatusAsync(exchange.OrganizerEmail, exchange.HatId, HatStatus.Closed);

        // act
        var sent = await _sut.ExecuteAsync(Schedule(exchange));

        // assert
        sent.Should().BeFalse();
        _sent.Should().BeEmpty();
    }

    /// <summary>
    /// A schedule is created when invitations go out and fires hours later. The exchange it names
    /// can be deleted in between, and the schedule has no way of knowing.
    /// </summary>
    [Fact]
    public async Task AHatThatNoLongerExists_IsNotAnError()
    {
        // act
        var act = async () => await _sut.ExecuteAsync(new UndeliverableInvitationsScheduleRequest
        {
            HatId = Guid.CreateVersion7(),
            OrganizerEmail = "organizer@example.com"
        });

        // assert
        await act.Should().NotThrowAsync();
        _sent.Should().BeEmpty();
    }

    /// <summary>
    /// Every read in the provider is scoped by the organizer's address, and this is what proves the
    /// schedule payload cannot be used to read somebody else's exchange.
    /// </summary>
    [Fact]
    public async Task AHatBelongingToSomebodyElse_IsNotRead()
    {
        // arrange
        var exchange = await SeedAsync();

        await BounceAsync(exchange.BadId, "smtp; 550 5.1.1 user unknown");

        // act
        var sent = await _sut.ExecuteAsync(new UndeliverableInvitationsScheduleRequest
        {
            HatId = exchange.HatId,
            OrganizerEmail = "somebody.else@example.com"
        });

        // assert
        sent.Should().BeFalse();
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SeveralFailures_AreListedTogetherInOneEmail()
    {
        // arrange
        var exchange = await SeedAsync();

        await BounceAsync(exchange.BadId, "smtp; 550 5.1.1 user unknown");
        await BounceAsync(exchange.QuietId, "smtp; 552 5.2.2 mailbox full");

        // act
        await _sut.ExecuteAsync(Schedule(exchange));

        // assert: one email, not one per participant. This is a summary of a send, and an organizer
        // receiving five of them would treat the fifth as spam.
        var notice = SentMessages().Should().ContainSingle().Subject;

        notice.HtmlBody.Should().Contain(exchange.BadEmail).And.Contain(exchange.QuietEmail);
        notice.Subject.Should().Contain("2 invitations");
    }

    /// <summary>
    /// This email is administrative. An organizer who is also a participant must not learn from it
    /// something their own invitation was written to keep from them.
    /// </summary>
    [Fact]
    public async Task TheNotice_SaysNothingAboutWhoDrewWhom()
    {
        // arrange
        var exchange = await SeedAsync();

        await _provider.UpdateParticipantPickedRecipientAsync(
            exchange.OrganizerEmail,
            exchange.HatId,
            exchange.BadName,
            exchange.QuietName);

        await BounceAsync(exchange.BadId, "smtp; 550 5.1.1 user unknown");

        // act
        await _sut.ExecuteAsync(Schedule(exchange));

        // assert: the person who bounced is named, because their address is the subject of the
        // email. Who they drew is not in it.
        var notice = SentMessages().Single();

        notice.HtmlBody.Should().Contain(exchange.BadName);
        notice.HtmlBody.Should().NotContain(exchange.QuietName);
    }

    private static UndeliverableInvitationsScheduleRequest Schedule(Exchange exchange) =>
        new() { HatId = exchange.HatId, OrganizerEmail = exchange.OrganizerEmail };

    /// <summary>
    /// One exchange with two participants beyond the organizer: one whose invitation the tests do
    /// something to, and one nothing is ever heard about.
    /// </summary>
    private async Task<Exchange> SeedAsync()
    {
        var hat = _hatFaker.Generate();
        await _provider.CreateHatAsync(hat);

        var bad = await AddParticipantAsync(hat, []);
        var quiet = await AddParticipantAsync(hat, [bad]);

        await _provider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.InvitationsSent);

        var ids = await _provider.GetParticipantIdsByEmailAsync(hat.HatId);

        return new Exchange
        {
            HatId = hat.HatId,
            OrganizerEmail = hat.OrganizerEmail,
            BadName = bad.Person.Name,
            BadEmail = bad.Person.Email,
            BadId = ids[bad.Person.Email],
            QuietName = quiet.Person.Name,
            QuietEmail = quiet.Person.Email,
            QuietId = ids[quiet.Person.Email]
        };
    }

    private Task<Participant> AddParticipantAsync(HatDataModel hat, ImmutableList<Participant> existing) =>
        _provider.CreateParticipantAsync(
            _participantFaker.Generate() with { HatId = hat.HatId, OrganizerEmail = hat.OrganizerEmail },
            existing);

    private Task BounceAsync(
        Guid participantId,
        string diagnosticCode,
        string? messageType = null
    ) =>
        _deliveryEvents.ProcessRecordAsync(Message($$"""
            {
              "eventType": "Bounce",
              "mail": {
                "messageId": "{{MessageId()}}",
                "timestamp": "2026-08-28T10:00:00.000Z",
                {{Tags(participantId, messageType ?? EmailMessageType.Invitation)}}
              },
              "bounce": {
                "bounceType": "Permanent",
                "bounceSubType": "General",
                "timestamp": "2026-08-28T10:00:06.000Z",
                "bouncedRecipients": [
                  { "emailAddress": "someone@example.com", "diagnosticCode": "{{diagnosticCode}}" }
                ]
              }
            }
            """));

    private Task DeliverAsync(Guid participantId, string occurredAt = "2026-08-28T10:00:04.000Z") =>
        _deliveryEvents.ProcessRecordAsync(Message($$"""
            {
              "eventType": "Delivery",
              "mail": {
                "messageId": "{{MessageId()}}",
                "timestamp": "2026-08-28T10:00:00.000Z",
                {{Tags(participantId, EmailMessageType.Invitation)}}
              },
              "delivery": { "timestamp": "{{occurredAt}}", "smtpResponse": "250 2.0.0 OK" }
            }
            """));

    private Task ComplainAsync(Guid participantId) =>
        _deliveryEvents.ProcessRecordAsync(Message($$"""
            {
              "eventType": "Complaint",
              "mail": {
                "messageId": "{{MessageId()}}",
                "timestamp": "2026-08-28T10:00:00.000Z",
                {{Tags(participantId, EmailMessageType.Invitation)}}
              },
              "complaint": {
                "timestamp": "2026-08-28T10:05:00.000Z",
                "complaintFeedbackType": "abuse"
              }
            }
            """));

    private Task DelayAsync(Guid participantId) =>
        _deliveryEvents.ProcessRecordAsync(Message($$"""
            {
              "eventType": "DeliveryDelay",
              "mail": {
                "messageId": "{{MessageId()}}",
                "timestamp": "2026-08-28T10:00:00.000Z",
                {{Tags(participantId, EmailMessageType.Invitation)}}
              },
              "deliveryDelay": {
                "timestamp": "2026-08-28T10:20:00.000Z",
                "delayType": "TransientCommunicationFailure"
              }
            }
            """));

    private static SQSEvent.SQSMessage Message(string body) => new() { Body = body };

    /// <summary>Shaped like the real thing, which is a long opaque string rather than a UUID.</summary>
    private static string MessageId() =>
        $"0100019{Guid.NewGuid():N}-{Guid.NewGuid():N}"[..60];

    private static string Tags(Guid participantId, string messageType) =>
        $$"""
          "tags": {
            "ses:configuration-set": ["giftexchange-outbound"],
            "participant_id": ["{{participantId}}"],
            "message_type": ["{{messageType}}"]
          }
          """;

    private ImmutableList<MimeMessage> SentMessages() =>
        [.. _sent.Select(raw => MimeMessage.Load(new MemoryStream(raw)))];

    private record Exchange
    {
        public required Guid HatId { get; init; }

        public required string OrganizerEmail { get; init; }

        public required string BadName { get; init; }

        public required string BadEmail { get; init; }

        public required Guid BadId { get; init; }

        public required string QuietName { get; init; }

        public required string QuietEmail { get; init; }

        public required Guid QuietId { get; init; }
    }
}
