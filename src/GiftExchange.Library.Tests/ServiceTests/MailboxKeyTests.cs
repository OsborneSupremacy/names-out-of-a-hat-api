using GiftExchange.Library.Extensions;

namespace GiftExchange.Library.Tests.ServiceTests;

public class MailboxKeyTests
{
    [Theory]
    // A +tag is a label on one inbox, not a second inbox.
    [InlineData("me+1@example.com", "me@example.com")]
    [InlineData("me+one+two@example.com", "me@example.com")]
    // Gmail ignores dots, and googlemail.com is the same service.
    [InlineData("o.s.b.o.r.n.e@gmail.com", "osborne@gmail.com")]
    [InlineData("o.sborne+hat@googlemail.com", "osborne@gmail.com")]
    // Everything ToNormalizedEmail already does still happens.
    [InlineData("  Me+X@Example.COM ", "me@example.com")]
    public void ToMailboxKey_GivenAnotherSpellingOfOneInbox_ReturnsTheSameKey(string email, string expected) =>
        email.ToMailboxKey().Should().Be(expected);

    [Theory]
    // Dots are significant everywhere but Gmail.
    [InlineData("first.last@example.com")]
    // Nothing before the +: stripping it would leave no mailbox at all.
    [InlineData("+tag@example.com")]
    // Nothing to take apart is not a reason to throw.
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    public void ToMailboxKey_GivenNothingToFold_ReturnsTheNormalizedAddress(string email) =>
        email.ToMailboxKey().Should().Be(email.ToNormalizedEmail());
}
