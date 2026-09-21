using Amazon.Lambda.SQSEvents;
using GiftExchange.Library.Contexts;
using GiftExchange.Library.Entities;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The delivery event path end to end, against a real database.
///
/// The bodies below are written as JSON rather than as objects, deliberately. Half of what this
/// path does is understand a wire format belonging to somebody else — the tags SES echoes back, the
/// timestamps that live in a different place for each event type, the fields that are simply absent
/// on some of them — and a test that handed the service an already-parsed object would assert only
/// that the test knows its own arrangement.
///
/// The provider is the real one for the reason ShareGiftIdeasServiceTests gives: what is worth
/// pinning down is a message id and a tag resolving to a row, which a stub cannot demonstrate.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DeliveryEventsServiceTests
{
    static DeliveryEventsServiceTests() => DotEnv.Load();

    private readonly GiftExchangeProvider _provider;

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly DeliveryEventsService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public DeliveryEventsServiceTests(PostgresFixture dbFixture)
    {
        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _sut = serviceProvider.GetRequiredService<DeliveryEventsService>();
    }

    [Fact]
    public async Task ADeliveryEvent_IsRecordedAgainstTheParticipantItsTagNames()
    {
        // arrange
        var (hat, participantId, _) = await ParticipantAsync();
        var messageId = MessageId();

        // act
        var written = await _sut.ProcessRecordAsync(
            Message(Delivery(messageId, participantId, EmailMessageType.Invitation)));

        // assert
        written.Should().BeTrue();

        var row = await SingleRowAsync(messageId);
        row.ParticipantId.Should().Be(participantId);
        row.Status.Should().Be(DeliveryStatus.Delivered);
        row.MessageType.Should().Be(EmailMessageType.Invitation);
        row.Detail.Should().BeEmpty("a delivery has nothing to explain");

        // and it reaches the organizer's view rather than only the table
        var (exists, stored) = await _provider.GetHatAsync(hat.OrganizerEmail, hat.HatId);
        exists.Should().BeTrue();

        var participant = stored.Participants.Single();
        participant.DeliveryStatus.Should().Be(DeliveryStatus.Delivered);

        // The two facts that make "delivered" mean something to an organizer whose participant says
        // they never saw it: which message arrived, and when. SES's own delivery timestamp rather
        // than the mail one -- four seconds later here, which is the point of preferring it.
        participant.DeliveryMessageType.Should().Be(EmailMessageType.Invitation);
        participant.DeliveryOccurredAt.Should().Be(DateTimeOffset.Parse("2026-08-28T10:00:04.000Z"));
    }

    [Fact]
    public async Task AParticipantNothingHasBeenHeardAbout_HasAnEmptyStatusRatherThanAFailedOne()
    {
        // arrange
        var (hat, _, _) = await ParticipantAsync();

        // act
        var (exists, stored) = await _provider.GetHatAsync(hat.OrganizerEmail, hat.HatId);

        // assert: the distinction the whole feature turns on. Nothing heard is not "not delivered".
        exists.Should().BeTrue();
        stored.Participants.Single().DeliveryStatus.Should().Be(DeliveryStatus.Unknown);
        stored.Participants.Single().DeliveryDetail.Should().BeEmpty();
        stored.Participants.Single().DeliveryMessageType.Should().BeEmpty();

        // The minimum date rather than now, so that whatever renders it can tell "no timestamp"
        // from a real one and leave the line out rather than dating it to the first century.
        stored.Participants.Single().DeliveryOccurredAt.Should().Be(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task ABounce_RecordsWhatTheReceivingServerSaid()
    {
        // arrange
        var (_, participantId, _) = await ParticipantAsync();
        var messageId = MessageId();

        // act
        await _sut.ProcessRecordAsync(Message(Bounce(
            messageId,
            participantId,
            bounceType: "Permanent",
            bounceSubType: "General",
            diagnosticCode: "smtp; 550 5.1.1 user unknown")));

        // assert: the part an organizer can act on is the sentence, not the status.
        var row = await SingleRowAsync(messageId);
        row.Status.Should().Be(DeliveryStatus.Bounced);
        row.Detail.Should().Be("Permanent/General: smtp; 550 5.1.1 user unknown");
    }

    [Fact]
    public async Task ASendFollowedByADelivery_MovesTheRowForwards()
    {
        // arrange
        var (_, participantId, _) = await ParticipantAsync();
        var messageId = MessageId();

        // act
        await _sut.ProcessRecordAsync(Message(Send(messageId, participantId)));
        await _sut.ProcessRecordAsync(Message(Delivery(messageId, participantId, EmailMessageType.Invitation)));

        // assert: one row, not two. The message id is what both events are about.
        var row = await SingleRowAsync(messageId);
        row.Status.Should().Be(DeliveryStatus.Delivered);
    }

    /// <summary>
    /// Neither SES nor SNS orders what it publishes, so this is an ordinary occurrence rather than
    /// an edge case: the Send can be handed to us after the Delivery it preceded.
    /// </summary>
    [Fact]
    public async Task ASendArrivingAfterTheDelivery_DoesNotMoveTheRowBackwards()
    {
        // arrange
        var (_, participantId, _) = await ParticipantAsync();
        var messageId = MessageId();

        await _sut.ProcessRecordAsync(Message(Delivery(messageId, participantId, EmailMessageType.Invitation)));

        // act
        var written = await _sut.ProcessRecordAsync(Message(Send(messageId, participantId)));

        // assert
        written.Should().BeFalse("the row already got further than this event says");
        (await SingleRowAsync(messageId)).Status.Should().Be(DeliveryStatus.Delivered);
    }

    /// <summary>
    /// A complaint happens after the message arrived, so it has to be able to overwrite the
    /// delivery it followed — the one case where a later event both outranks and contradicts the
    /// success before it.
    /// </summary>
    [Fact]
    public async Task AComplaintAfterADelivery_Wins()
    {
        // arrange
        var (_, participantId, _) = await ParticipantAsync();
        var messageId = MessageId();

        await _sut.ProcessRecordAsync(Message(Delivery(messageId, participantId, EmailMessageType.Invitation)));

        // act
        await _sut.ProcessRecordAsync(Message(Complaint(messageId, participantId)));

        // assert
        (await SingleRowAsync(messageId)).Status.Should().Be(DeliveryStatus.Complained);
    }

    /// <summary>
    /// Marking a message as spam blocks the address from every exchange, so the next organizer is
    /// refused before sending rather than told afterwards. Delivered twice to show the retry SQS may
    /// make is harmless.
    /// </summary>
    [Fact]
    public async Task AComplaint_BlocksTheComplainantFromBeingAddedAnywhere()
    {
        // arrange
        var (_, participantId, email) = await ParticipantAsync();
        var messageId = MessageId();

        // act
        await _sut.ProcessRecordAsync(Message(Complaint(messageId, participantId, email.ToUpperInvariant())));
        await _sut.ProcessRecordAsync(Message(Complaint(messageId, participantId, email.ToUpperInvariant())));

        // assert
        (await BlockedAnywhereCountAsync(email)).Should().Be(1);
        (await _provider.FindBlockedAnywhereAsync([email.Trim().ToLowerInvariant()])).Should().ContainSingle();
    }

    /// <summary>
    /// Most feedback loops send no feedback type at all, so its absence has to block like "abuse".
    /// </summary>
    [Fact]
    public async Task AComplaintWithNoFeedbackType_StillBlocks()
    {
        // arrange
        var (_, participantId, email) = await ParticipantAsync();

        // act
        await _sut.ProcessRecordAsync(Message(Complaint(MessageId(), participantId, email, feedbackType: "")));

        // assert
        (await BlockedAnywhereCountAsync(email)).Should().Be(1);
    }

    /// <summary>"not-spam" is the recipient taking a complaint back, not making one.</summary>
    [Fact]
    public async Task ANotSpamReport_DoesNotBlock()
    {
        // arrange
        var (_, participantId, email) = await ParticipantAsync();

        // act
        await _sut.ProcessRecordAsync(Message(Complaint(MessageId(), participantId, email, feedbackType: "not-spam")));

        // assert
        (await BlockedAnywhereCountAsync(email)).Should().Be(0);
    }

    /// <summary>
    /// The tag is how an event finds an organizer's row; the complaint is about the address, so a
    /// send that forgot its tag must still block.
    /// </summary>
    [Fact]
    public async Task AComplaintWithNoParticipantTag_StillBlocks()
    {
        // arrange
        var email = $"{Guid.NewGuid():N}@example.com";

        // act
        var written = await _sut.ProcessRecordAsync(Message(Complaint(MessageId(), Guid.Empty, email)));

        // assert
        written.Should().BeFalse("there is no participant row to write");
        (await BlockedAnywhereCountAsync(email)).Should().Be(1);
    }

    [Fact]
    public async Task AnEventWithNoParticipantTag_IsIgnoredRatherThanThrown()
    {
        // arrange: a send from some future code path that forgot to tag itself.
        var messageId = MessageId();
        var body = $$"""
            {
              "eventType": "Delivery",
              "mail": { "messageId": "{{messageId}}", "timestamp": "2026-08-28T10:00:00.000Z", "tags": {} },
              "delivery": { "timestamp": "2026-08-28T10:00:04.000Z" }
            }
            """;

        // act
        var written = await _sut.ProcessRecordAsync(Message(body));

        // assert: the message really was sent, so there is nothing to retry and nothing to record.
        written.Should().BeFalse();
        await using var context = await _contextFactory.CreateDbContextAsync();
        (await context.ParticipantEmailDeliveries.AnyAsync(row => row.SesMessageId == messageId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task AnEventTypeNothingHereRecords_IsIgnored()
    {
        // arrange
        var (_, participantId, _) = await ParticipantAsync();
        var messageId = MessageId();
        var body = $$"""
            {
              "eventType": "Open",
              "mail": {
                "messageId": "{{messageId}}",
                "timestamp": "2026-08-28T10:00:00.000Z",
                "tags": { "participant_id": ["{{participantId}}"], "message_type": ["INVITATION"] }
              }
            }
            """;

        // act
        var written = await _sut.ProcessRecordAsync(Message(body));

        // assert
        written.Should().BeFalse();
    }

    /// <summary>
    /// The diagnostic is written by whatever server refused the message, so its length is nobody's
    /// to promise. Losing the status as well as the explanation would be the worse outcome.
    /// </summary>
    [Fact]
    public async Task AnOverlongDiagnostic_IsCutDownRatherThanFailingTheWrite()
    {
        // arrange
        var (_, participantId, _) = await ParticipantAsync();
        var messageId = MessageId();

        // act
        await _sut.ProcessRecordAsync(Message(Bounce(
            messageId,
            participantId,
            bounceType: "Permanent",
            bounceSubType: "General",
            diagnosticCode: new string('x', 900))));

        // assert
        var row = await SingleRowAsync(messageId);
        row.Status.Should().Be(DeliveryStatus.Bounced);
        row.Detail.Length.Should().Be(500);
    }

    /// <summary>
    /// Removing somebody and adding them back produces a new participant. Carrying the old one's
    /// delivery history over would be a claim about a message never sent to them.
    /// </summary>
    [Fact]
    public async Task RemovingAParticipant_TakesWhatWasHeardAboutThemWithIt()
    {
        // arrange
        var (hat, participantId, email) = await ParticipantAsync();
        var messageId = MessageId();

        await _sut.ProcessRecordAsync(Message(Delivery(messageId, participantId, EmailMessageType.Invitation)));

        // act
        await _provider.DeleteParticipantAsync(hat.OrganizerEmail, hat.HatId, email);

        // assert
        await using var context = await _contextFactory.CreateDbContextAsync();
        (await context.ParticipantEmailDeliveries.AnyAsync(row => row.ParticipantId == participantId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ABodyThatIsNotAnEventAtAll_Throws()
    {
        // arrange: what a subscription without raw message delivery would put on the queue is not
        // this, but a wiring mistake should end in the dead letter queue rather than in silence.
        var record = new SQSEvent.SQSMessage { Body = "null" };

        // act
        var act = async () => await _sut.ProcessRecordAsync(record);

        // assert
        await act.Should().ThrowAsync<AggregateException>();
    }

    private async Task<(HatDataModel hat, Guid participantId, string email)> ParticipantAsync()
    {
        var hat = _hatFaker.Generate();
        await _provider.CreateHatAsync(hat);

        var request = _participantFaker.Generate() with
        {
            HatId = hat.HatId,
            OrganizerEmail = hat.OrganizerEmail
        };

        var participant = await _provider.CreateParticipantAsync(request, []);

        var ids = await _provider.GetParticipantIdsByEmailAsync(hat.HatId);

        return (hat, ids[participant.Person.Email], participant.Person.Email);
    }

    private async Task<ParticipantEmailDeliveryEntity> SingleRowAsync(string messageId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.ParticipantEmailDeliveries
            .AsNoTracking()
            .SingleAsync(row => row.SesMessageId == messageId);
    }

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

    /// <summary>A Send carries no timestamp of its own, so the mail timestamp has to serve.</summary>
    private static string Send(string messageId, Guid participantId) =>
        $$"""
          {
            "eventType": "Send",
            "mail": {
              "messageId": "{{messageId}}",
              "timestamp": "2026-08-28T10:00:00.000Z",
              "destination": ["someone@example.com"],
              {{Tags(participantId, EmailMessageType.Invitation)}}
            },
            "send": {}
          }
          """;

    private static string Delivery(string messageId, Guid participantId, string messageType) =>
        $$"""
          {
            "eventType": "Delivery",
            "mail": {
              "messageId": "{{messageId}}",
              "timestamp": "2026-08-28T10:00:00.000Z",
              "destination": ["someone@example.com"],
              {{Tags(participantId, messageType)}}
            },
            "delivery": {
              "timestamp": "2026-08-28T10:00:04.000Z",
              "processingTimeMillis": 4000,
              "smtpResponse": "250 2.0.0 OK"
            }
          }
          """;

    private static string Bounce(
        string messageId,
        Guid participantId,
        string bounceType,
        string bounceSubType,
        string diagnosticCode
    ) =>
        $$"""
          {
            "eventType": "Bounce",
            "mail": {
              "messageId": "{{messageId}}",
              "timestamp": "2026-08-28T10:00:00.000Z",
              "destination": ["someone@example.com"],
              {{Tags(participantId, EmailMessageType.Invitation)}}
            },
            "bounce": {
              "bounceType": "{{bounceType}}",
              "bounceSubType": "{{bounceSubType}}",
              "timestamp": "2026-08-28T10:00:06.000Z",
              "bouncedRecipients": [
                { "emailAddress": "someone@example.com", "diagnosticCode": "{{diagnosticCode}}" }
              ]
            }
          }
          """;

    private static string Complaint(
        string messageId,
        Guid participantId,
        string email = "someone@example.com",
        string feedbackType = "abuse"
    ) =>
        $$"""
          {
            "eventType": "Complaint",
            "mail": {
              "messageId": "{{messageId}}",
              "timestamp": "2026-08-28T10:00:00.000Z",
              "destination": ["{{email}}"],
              {{Tags(participantId, EmailMessageType.Invitation)}}
            },
            "complaint": {
              "timestamp": "2026-08-29T09:00:00.000Z",
              "complaintFeedbackType": "{{feedbackType}}",
              "complainedRecipients": [{ "emailAddress": "{{email}}" }]
            }
          }
          """;

    private async Task<int> BlockedAnywhereCountAsync(string email)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var normalized = email.Trim().ToLowerInvariant();

        return await context.DoNotAddAnywhere.CountAsync(block => block.EmailNormalized == normalized);
    }
}
