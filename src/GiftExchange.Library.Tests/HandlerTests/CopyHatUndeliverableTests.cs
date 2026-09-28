using Amazon.Lambda.SQSEvents;

namespace GiftExchange.Library.Tests.HandlerTests;

/// <summary>
/// The refusal to copy an exchange that has an address known not to work.
///
/// Delivery is recorded through the real delivery event path rather than by writing rows, for the
/// reason UndeliverableInvitationsServiceTests gives: what matters is that an SES bounce arriving
/// the way one really does is what stops the copy, and that a complaint, which is not a bad
/// address, does not.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CopyHatUndeliverableTests
{
    private readonly JsonService _jsonService;

    private readonly ILambdaContext _context;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly DeliveryEventsService _deliveryEvents;

    private readonly HatDataModelFaker _hatDataModelFaker = new();

    private readonly AddParticipantRequestFaker _addParticipantRequestFaker = new();

    private readonly IApiGatewayHandler _sut;

    public CopyHatUndeliverableTests(PostgresFixture dbFixture)
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
        _giftExchangeProvider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _deliveryEvents = serviceProvider.GetRequiredService<DeliveryEventsService>();
        _sut = serviceProvider.GetRequiredKeyedService<IApiGatewayHandler>("post/hat/copy");
    }

    [Fact]
    public async Task CopyHat_WithEveryAddressWorking_CreatesTheCopy()
    {
        // arrange
        var source = await CreateClosedHatAsync();
        await RecordAsync(source.FirstId, "Delivery", """ "delivery": { "timestamp": "2026-08-28T10:00:04.000Z" } """);

        // act
        var response = await CopyAsync(source);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("Bounce", """ "bounce": { "bounceType": "Permanent", "bounceSubType": "General", "bouncedRecipients": [] } """)]
    [InlineData("Reject", """ "reject": { "reason": "Bad content" } """)]
    [InlineData("Rendering Failure", """ "failure": { "errorMessage": "Missing attribute" } """)]
    public async Task CopyHat_WithAnUndeliverableAddress_ReturnsConflictAndCreatesNothing(string eventType, string eventBody)
    {
        // arrange
        var source = await CreateClosedHatAsync();
        await RecordAsync(source.FirstId, eventType, eventBody);

        // act
        var response = await CopyAsync(source);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        response.Body.Should().Contain("not receiving emails");

        var (created, _) = await _giftExchangeProvider.DoesHatAlreadyExistAsync(source.Hat.OrganizerEmail, CopyName);
        created.Should().BeFalse();
    }

    /// <summary>
    /// The announcement is what a closed exchange sent last, so a bounce on it is the usual shape
    /// this takes by the time anybody presses copy.
    /// </summary>
    [Fact]
    public async Task CopyHat_WithABouncedAnnouncement_ReturnsConflict()
    {
        // arrange
        var source = await CreateClosedHatAsync();
        await RecordAsync(
            source.FirstId,
            "Bounce",
            """ "bounce": { "bounceType": "Permanent", "bounceSubType": "General", "bouncedRecipients": [] } """,
            EmailMessageType.Completion);

        // act
        var response = await CopyAsync(source);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A complaint means the message arrived. The address works; the person does not want the mail,
    /// and the do-not-add list already leaves them out of the copy without refusing it.
    /// </summary>
    [Fact]
    public async Task CopyHat_WithAComplaint_StillCopies()
    {
        // arrange
        var source = await CreateClosedHatAsync();
        await RecordAsync(
            source.FirstId,
            "Complaint",
            """ "complaint": { "timestamp": "2026-08-28T10:05:00.000Z", "complaintFeedbackType": "abuse", "complainedRecipients": [] } """);

        // act
        var response = await CopyAsync(source);

        // assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Created);
    }

    private const string CopyName = "The Copy";

    private Task<APIGatewayProxyResponse> CopyAsync(SourceHat source) =>
        _sut.FunctionHandler(
            _jsonService.SerializeDefault(new CopyHatRequest
            {
                OrganizerEmail = source.Hat.OrganizerEmail,
                HatId = source.Hat.HatId,
                NewHatName = CopyName,
                ExcludePreviousRecipients = false
            }).ToApiGatewayProxyRequest(source.Hat.OrganizerEmail),
            _context);

    private async Task<SourceHat> CreateClosedHatAsync()
    {
        var hat = _hatDataModelFaker.Generate();
        await _giftExchangeProvider.CreateHatAsync(hat);

        var first = await AddParticipantAsync(hat, []);
        await AddParticipantAsync(hat, [first]);

        await _giftExchangeProvider.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.Closed);

        var ids = await _giftExchangeProvider.GetParticipantIdsByEmailAsync(hat.HatId);

        return new SourceHat(hat, ids[first.Person.Email]);
    }

    private Task<Participant> AddParticipantAsync(HatDataModel hat, ImmutableList<Participant> existing) =>
        _giftExchangeProvider.CreateParticipantAsync(
            _addParticipantRequestFaker.Generate() with { HatId = hat.HatId, OrganizerEmail = hat.OrganizerEmail },
            existing);

    private Task RecordAsync(Guid participantId, string eventType, string eventBody, string? messageType = null) =>
        _deliveryEvents.ProcessRecordAsync(new SQSEvent.SQSMessage
        {
            Body = $$"""
                {
                  "eventType": "{{eventType}}",
                  "mail": {
                    "messageId": "{{Guid.NewGuid():N}}",
                    "timestamp": "2026-08-28T10:00:00.000Z",
                    "tags": {
                      "participant_id": ["{{participantId}}"],
                      "message_type": ["{{messageType ?? EmailMessageType.Invitation}}"]
                    }
                  },
                  {{eventBody}}
                }
                """
        });

    private record SourceHat(HatDataModel Hat, Guid FirstId);
}
