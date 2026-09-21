using GiftExchange.Library.Validators;

namespace GiftExchange.Library.Tests.ServiceTests;

public class DisposableEmailDomainsTests
{
    [Theory]
    [InlineData("someone@mailinator.com")]
    // Case and stray whitespace are not a way around it.
    [InlineData("Someone@MAILINATOR.com")]
    [InlineData("someone@mailinator.com ")]
    // Some services issue a fresh subdomain per inbox, so the parent domain has to catch them.
    [InlineData("someone@x7f2.mailinator.com")]
    // A fully qualified domain with its trailing dot is still the same domain.
    [InlineData("someone@mailinator.com.")]
    public void IsDisposable_GivenAThrowawayAddress_ReturnsTrue(string email) =>
        DisposableEmailDomains.IsDisposable(email).Should().BeTrue();

    [Theory]
    [InlineData("someone@gmail.com")]
    [InlineData("someone@outlook.com")]
    [InlineData("someone@icloud.com")]
    // Forwarding services hide a real inbox rather than a throwaway one, and upstream leaves them
    // out on purpose. These guard against a list update that stops doing so.
    [InlineData("someone@privaterelay.appleid.com")]
    [InlineData("someone@mozmail.com")]
    [InlineData("someone@simplelogin.com")]
    [InlineData("someone@duck.com")]
    // Only a whole label matches: a domain merely ending in the same letters is somebody else's.
    [InlineData("someone@xmailinator.com")]
    // Nothing to look up is not a reason to throw.
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("someone@")]
    public void IsDisposable_GivenAnyOtherAddress_ReturnsFalse(string email) =>
        DisposableEmailDomains.IsDisposable(email).Should().BeFalse();

    [Fact]
    public void RequestMagicLinkValidator_GivenAThrowawayAddress_SaysWhy()
    {
        // act
        var result = new RequestMagicLinkRequestValidator()
            .Validate(new RequestMagicLinkRequest { Email = "someone@mailinator.com" });

        // assert
        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be(DisposableEmailDomains.RejectionMessage);
    }

    [Fact]
    public void RequestMagicLinkValidator_GivenAnOrdinaryAddress_Passes() =>
        new RequestMagicLinkRequestValidator()
            .Validate(new RequestMagicLinkRequest { Email = "someone@gmail.com" })
            .IsValid.Should().BeTrue();
}
