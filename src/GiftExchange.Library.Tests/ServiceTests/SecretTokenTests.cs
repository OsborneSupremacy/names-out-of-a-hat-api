using GiftExchange.Library.Utility;

namespace GiftExchange.Library.Tests.ServiceTests;

public class SecretTokenTests
{
    [Fact]
    public void Create_ReturnsADifferentTokenEveryTime()
    {
        // act
        var tokens = Enumerable.Range(0, 1000).Select(_ => SecretToken.Create()).ToList();

        // assert
        tokens.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Create_ProducesSomethingAnEmailAddressCanCarryUnquoted()
    {
        // act
        var tokens = Enumerable.Range(0, 100).Select(_ => SecretToken.Create()).ToList();

        // assert: base64url, so letters, digits, '-' and '_' only. Plain base64 would yield '+',
        // '/' and '=', none of which belong in an unquoted local part — and this token becomes one.
        tokens.Should().AllSatisfy(token =>
            token.Should().MatchRegex("^[A-Za-z0-9_-]+$"));
    }

    [Fact]
    public void Hash_IsStableForTheSameToken()
    {
        // arrange: a token arriving in a link is matched by hashing it and looking for that, so an
        // unstable digest would mean nobody could ever submit anything.
        var token = SecretToken.Create();

        // act & assert
        SecretToken.Hash(token).Should().Be(SecretToken.Hash(token));
    }

    [Fact]
    public void Hash_DiffersBetweenTokens()
    {
        // act & assert
        SecretToken.Hash(SecretToken.Create()).Should().NotBe(SecretToken.Hash(SecretToken.Create()));
    }

    [Fact]
    public void Hash_DoesNotGiveTheTokenBack()
    {
        // arrange
        var token = SecretToken.Create();

        // act
        var hash = SecretToken.Hash(token);

        // assert: what is stored must not be what was sent out. Hex-encoded SHA-256 is 64
        // characters, and the token is nowhere inside it.
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9A-F]{64}$");
        hash.Should().NotContain(token);
    }
}
