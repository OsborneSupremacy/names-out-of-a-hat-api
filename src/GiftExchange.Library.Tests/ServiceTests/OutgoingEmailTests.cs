namespace GiftExchange.Library.Tests.ServiceTests;

public class OutgoingEmailTests
{
    [Fact]
    public void Sender_GivenAName_SendsOnTheirBehalfThroughTheProduct()
    {
        // act
        var sender = OutgoingEmail.Sender("Jane Smith");

        // assert
        sender.Name.Should().Be("Jane Smith via Names Out Of A Hat");
        sender.Address.Should().Be(OutgoingEmail.SenderAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Sender_GivenNoName_IsTheProductAlone(string name)
    {
        // act
        var sender = OutgoingEmail.Sender(name);

        // assert
        sender.Name.Should().Be(OutgoingEmail.ProductName);
    }

    [Fact]
    public void Sender_GivenANameWithLineBreaks_PutsItOnOneLine()
    {
        // act
        var sender = OutgoingEmail.Sender("Jane\r\n  Smith\t");

        // assert
        sender.Name.Should().Be("Jane Smith via Names Out Of A Hat");
    }

    [Fact]
    public void Compose_GivesBothParts()
    {
        // act
        var message = OutgoingEmail.Compose(
            OutgoingEmail.Sender(string.Empty),
            "alice@example.com",
            "Subject",
            "<p>Hello &amp; welcome</p>");

        // assert
        message.HtmlBody.Should().Contain("<p>Hello &amp; welcome</p>");
        message.TextBody.Trim().Should().Be("Hello & welcome");
    }
}
