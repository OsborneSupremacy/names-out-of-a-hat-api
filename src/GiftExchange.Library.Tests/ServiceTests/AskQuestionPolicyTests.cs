namespace GiftExchange.Library.Tests.ServiceTests;

public class AskQuestionPolicyTests
{
    private readonly AskQuestionPolicy _sut = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_GivenNoQuestion_Accepts(string question) =>
        _sut.Check(question, "Ben").Should().Be(AskQuestionOutcome.Accepted);

    [Fact]
    public void Check_GivenAnOrdinaryQuestion_Accepts() =>
        _sut.Check("What shirt size do they wear?", "Ben").Should().Be(AskQuestionOutcome.Accepted);

    [Fact]
    public void Check_GivenExactlyTheLimit_Accepts() =>
        _sut.Check(new string('a', AskQuestionPolicy.MaxLength), "Ben").Should().Be(AskQuestionOutcome.Accepted);

    [Fact]
    public void Check_GivenOneOverTheLimit_Refuses() =>
        _sut.Check(new string('a', AskQuestionPolicy.MaxLength + 1), "Ben")
            .Should().Be(AskQuestionOutcome.RejectedTooLong);

    [Theory]
    [InlineData("Do they like jazz? Thanks, Ben")]
    [InlineData("do they like jazz? - BEN")]
    public void Check_GivenTheAskersName_Refuses(string question) =>
        _sut.Check(question, "Ben").Should().Be(AskQuestionOutcome.RejectedWouldRevealAsker);

    [Fact]
    public void Check_GivenANameThatOnlyContainsTheAskers_Accepts() =>
        _sut.Check("Would they like a Benjamin Moore paint voucher?", "Ben")
            .Should().Be(AskQuestionOutcome.Accepted);

    [Theory]
    [InlineData("Would they like this? https://example.com/thing")]
    [InlineData("Is www.example.com any good?")]
    [InlineData("Seen example.com/thing?")]
    public void Check_GivenALink_Refuses(string question) =>
        _sut.Check(question, "Ben").Should().Be(AskQuestionOutcome.RejectedContainsLink);
}
