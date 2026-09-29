using GiftExchange.Library.Utility;

namespace GiftExchange.Library.Tests.ServiceTests;

public class OriginGuardTests
{
    private const string Secret = "s3cret-from-cloudfront";

    [Fact]
    public void GivenTheSecret_Allows() =>
        new OriginGuard(Secret, enforced: true)
            .Check(Headers(Secret))
            .Should().Be(OriginVerdict.Allow);

    [Fact]
    public void GivenTheSecretUnderAnotherCasingOfTheHeaderName_Allows() =>
        new OriginGuard(Secret, enforced: true)
            .Check(new Dictionary<string, string> { ["x-origin-verify"] = Secret })
            .Should().Be(OriginVerdict.Allow);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("s3cret-from-cloudfronT")]
    [InlineData("s3cret")]
    public void GivenAnythingElse_WhenEnforced_Refuses(string? presented) =>
        new OriginGuard(Secret, enforced: true)
            .Check(presented is null ? new Dictionary<string, string>() : Headers(presented))
            .Should().Be(OriginVerdict.Refuse);

    [Fact]
    public void GivenNoHeadersAtAll_WhenEnforced_Refuses() =>
        new OriginGuard(Secret, enforced: true)
            .Check(null)
            .Should().Be(OriginVerdict.Refuse);

    [Fact]
    public void GivenNoSecret_WhenNotYetEnforced_ReportsButAllows() =>
        new OriginGuard(Secret, enforced: false)
            .Check(new Dictionary<string, string>())
            .Should().Be(OriginVerdict.AllowButReport);

    [Fact]
    public void WithNoSecretConfigured_ChecksNothing() =>
        new OriginGuard(string.Empty, enforced: true)
            .Check(new Dictionary<string, string>())
            .Should().Be(OriginVerdict.Allow);

    private static Dictionary<string, string> Headers(string value) =>
        new() { [OriginGuard.HeaderName] = value };
}
