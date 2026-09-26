using Amazon.Lambda.SQSEvents;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using MimeKit;
using NSubstitute;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// What a participant's mail client sees before they open anything: who it is from, and whether it
/// offers them a way out that isn't the spam button.
/// </summary>
public class InvitationQueueHandlerServiceTests
{
    private const string ConfigurationSet = "giftexchange-outbound";

    static InvitationQueueHandlerServiceTests()
    {
        DotEnv.Load();
        Environment.SetEnvironmentVariable("LIVE_MODE", "true");
        Environment.SetEnvironmentVariable("SES_CONFIGURATION_SET", ConfigurationSet);
    }

    private readonly IAmazonSimpleEmailService _ses = Substitute.For<IAmazonSimpleEmailService>();

    private readonly JsonService _jsonService;

    private readonly InvitationQueueHandlerService _sut;

    private SendRawEmailRequest? _sent;

    private MimeMessage? _message;

    public InvitationQueueHandlerServiceTests()
    {
        _jsonService = new ServiceCollection()
            .AddUtilities()
            .BuildServiceProvider()
            .GetRequiredService<JsonService>();

        _ses.SendRawEmailAsync(Arg.Any<SendRawEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sent = (SendRawEmailRequest)call[0];
                _sent.RawMessage.Data.Position = 0;
                _message = MimeMessage.Load(_sent.RawMessage.Data);
                return new SendRawEmailResponse { MessageId = "message-id" };
            });

        _sut = new InvitationQueueHandlerService(_ses, _jsonService);
    }

    [Fact]
    public async Task AnInvitation_IsFromTheOrganizerViaTheProduct()
    {
        // act
        await SendAsync(Invitation() with { SenderName = "Jane Smith" });

        // assert
        var from = _message!.From.Mailboxes.Single();
        from.Name.Should().Be("Jane Smith via Names Out Of A Hat");
        from.Address.Should().Be(OutgoingEmail.SenderAddress);
    }

    [Fact]
    public async Task AnInvitation_CarriesAPlainTextPartAsWellAsHtml()
    {
        // act
        await SendAsync(Invitation());

        // assert
        _message!.HtmlBody.Should().Contain("<b>Charlie</b>");
        _message.TextBody.Should().Contain("Charlie").And.NotContain("<b>");
    }

    [Fact]
    public async Task AnInvitationWithALeaveLink_OffersItAsTheUnsubscribeAndNotAsOneClick()
    {
        // arrange
        const string leaveUrl = "https://api.namesoutofahat.com/leave/leave-token";

        // act
        await SendAsync(Invitation() with { UnsubscribeUrl = leaveUrl });

        // assert: a one-click POST to the leave link would be the leave itself, with no human on
        // the page, so the Post header must never appear.
        _message!.Headers["List-Unsubscribe"].Should().Be($"<{leaveUrl}>");
        _message.Headers.Contains("List-Unsubscribe-Post").Should().BeFalse();
    }

    [Fact]
    public async Task AMessageWithNoLeaveLink_HasNoUnsubscribeHeader()
    {
        // act
        await SendAsync(Invitation());

        // assert
        _message!.Headers.Contains("List-Unsubscribe").Should().BeFalse();
    }

    [Fact]
    public async Task TheSend_KeepsItsTagsAndConfigurationSet()
    {
        // arrange
        var invitation = Invitation();

        // act
        await SendAsync(invitation);

        // assert: without these, delivery events stop reaching the participant's row.
        _sent!.ConfigurationSetName.Should().Be(ConfigurationSet);
        _sent.Tags.Should().ContainEquivalentOf(
            new MessageTag { Name = SesMessageTags.ParticipantId, Value = invitation.ParticipantId.ToString() });
        _sent.Tags.Should().ContainEquivalentOf(
            new MessageTag { Name = SesMessageTags.MessageType, Value = EmailMessageType.Invitation });
    }

    [Fact]
    public async Task AMessageQueuedBeforeTheSenderFieldsExisted_StillSendsFromTheProduct()
    {
        // arrange: the JSON the previous deployment put on the queue, which has neither field.
        var participantId = Guid.CreateVersion7();
        var body =
            $$"""
              {"HatId":"{{Guid.CreateVersion7()}}","OrganizerEmail":"ben@example.com","RecipientEmail":"alice@example.com","ParticipantId":"{{participantId}}","MessageType":"INVITATION","Subject":"Hello","HtmlBody":"<p>Hi</p>"}
              """;

        // act
        await _sut.ProcessRecordAsync(new SQSEvent.SQSMessage { Body = body }, LambdaContext());

        // assert
        _message!.From.Mailboxes.Single().Name.Should().Be(OutgoingEmail.ProductName);
        _message.Headers.Contains("List-Unsubscribe").Should().BeFalse();
    }

    private Task SendAsync(GiftExchangeEmailRequest invitation) =>
        _sut.ProcessRecordAsync(
            new SQSEvent.SQSMessage { Body = _jsonService.SerializeDefault(invitation) },
            LambdaContext());

    /// <summary>A substitute rather than the shared fake, because this handler logs through it.</summary>
    private static ILambdaContext LambdaContext()
    {
        var context = Substitute.For<ILambdaContext>();
        context.Logger.Returns(Substitute.For<ILambdaLogger>());
        return context;
    }

    private static GiftExchangeEmailRequest Invitation() =>
        new()
        {
            HatId = Guid.CreateVersion7(),
            OrganizerEmail = "ben@example.com",
            RecipientEmail = "alice@example.com",
            ParticipantId = Guid.CreateVersion7(),
            MessageType = EmailMessageType.Invitation,
            Subject = "Ben has added you to the gift exchange!",
            HtmlBody = "Dear Alice,<br /><br /><b>Charlie</b><br /><br />"
        };
}
