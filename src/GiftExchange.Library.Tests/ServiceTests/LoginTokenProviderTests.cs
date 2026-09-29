using System.Security.Cryptography;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// Magic link tokens: how they are stored, and what makes one usable exactly once.
///
/// Written as characterisation tests before this class was refactored to share its token encoding
/// with <see cref="SecretToken"/>, because it had no coverage at all and it is the sign-in path —
/// getting it wrong locks everybody out of the application, and does so quietly, since a token
/// that fails to match is indistinguishable from an expired one.
/// </summary>
public class LoginTokenProviderTests
{
    static LoginTokenProviderTests()
    {
        // The constructor reads TABLE_NAME, and field initialisers run before any constructor body.
        DotEnv.Load();
    }

    private readonly IAmazonDynamoDB _dynamoDb = Substitute.For<IAmazonDynamoDB>();

    private readonly LoginTokenProvider _sut;

    public LoginTokenProviderTests() => _sut = new LoginTokenProvider(_dynamoDb);

    [Fact]
    public async Task CreateLoginTokenAsync_StoresTheHashAndNeverTheToken()
    {
        // act
        var token = await _sut.CreateLoginTokenAsync("Ben@Example.com");

        // assert: the whole point of the class. A dump of the table must not let anybody redeem a
        // pending link, so what is written is a digest the token cannot be recovered from.
        var item = CapturedPut().Item;

        item["PK"].S.Should().Be($"LOGIN#{Sha256Hex(token)}");
        item["PK"].S.Should().NotContain(token);
        item.Values.Select(value => value.S).Should().NotContain(token);
    }

    [Fact]
    public async Task CreateLoginTokenAsync_NormalizesTheAddressItStores()
    {
        // act
        await _sut.CreateLoginTokenAsync("  Ben@Example.COM  ");

        // assert: redemption hands this address straight to session issuing, so the casing stored
        // here is the casing everything downstream sees.
        CapturedPut().Item["Email"].S.Should().Be("ben@example.com");
    }

    [Fact]
    public async Task CreateLoginTokenAsync_ExpiresTheItemInFifteenMinutes()
    {
        // act
        await _sut.CreateLoginTokenAsync("ben@example.com");

        // assert
        var item = CapturedPut().Item;
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(item["ExpiresAt"].N));

        expiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(30));

        // ttl is what DynamoDB reaps on; ExpiresAt is what redemption checks. They have to agree,
        // or a token outlives the check that was supposed to bound it.
        item["ttl"].N.Should().Be(item["ExpiresAt"].N);
    }

    [Fact]
    public async Task CreateLoginTokenAsync_ReturnsADifferentTokenEveryTime()
    {
        // act
        var tokens = new List<string>();

        for (var i = 0; i < 50; i++)
            tokens.Add(await _sut.CreateLoginTokenAsync("ben@example.com"));

        // assert
        tokens.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task CreateLoginTokenAsync_ReturnsSomethingSafeToPutInALink()
    {
        // act
        var token = await _sut.CreateLoginTokenAsync("ben@example.com");

        // assert: base64url. Plain base64 would yield '+' and '/', which do not survive a round
        // trip through a query string without encoding.
        token.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public async Task CreateLoginTokenAsync_DrawsTheFullTwoHundredAndFiftySixBits()
    {
        // act
        var token = await _sut.CreateLoginTokenAsync("ben@example.com");

        // assert: 32 random bytes, which base64url renders as 43 characters once the padding is
        // stripped. Pinned because the encoding is now shared with the gift ideas routing tokens,
        // which are deliberately half this length so they stay readable inside an email address —
        // and picking up their default here would quietly halve the entropy of every sign-in link
        // without changing anything visible.
        token.Should().HaveLength(43);
    }

    [Fact]
    public async Task TryRedeemLoginTokenAsync_ConsumesTheTokenAndReturnsItsAddress()
    {
        // arrange
        GivenTheStoredItem(("Email", new AttributeValue { S = "ben@example.com" }), Expiring(inMinutes: 5));

        // act
        var (redeemed, email) = await _sut.TryRedeemLoginTokenAsync("a-token");

        // assert
        redeemed.Should().BeTrue();
        email.Should().Be("ben@example.com");
    }

    [Fact]
    public async Task TryRedeemLoginTokenAsync_LooksTheTokenUpByItsHash()
    {
        // arrange
        GivenTheStoredItem(("Email", new AttributeValue { S = "ben@example.com" }), Expiring(inMinutes: 5));

        // act
        await _sut.TryRedeemLoginTokenAsync("a-token");

        // assert: the plaintext is never sent to DynamoDB, because nothing there is stored under it.
        CapturedDelete().Key["PK"].S.Should().Be($"LOGIN#{Sha256Hex("a-token")}");
    }

    [Fact]
    public async Task TryRedeemLoginTokenAsync_DeletesConditionallySoATokenWorksOnce()
    {
        // arrange
        GivenTheStoredItem(("Email", new AttributeValue { S = "ben@example.com" }), Expiring(inMinutes: 5));

        // act
        await _sut.TryRedeemLoginTokenAsync("a-token");

        // assert: single use rests entirely on this. A plain delete would let two concurrent
        // redemptions of the same link both succeed, and the old item has to come back or there is
        // no address to issue a session for.
        var request = CapturedDelete();

        request.ConditionExpression.Should().Be("attribute_exists(PK)");
        request.ReturnValues.Should().Be(ReturnValue.ALL_OLD);
    }

    [Fact]
    public async Task TryRedeemLoginTokenAsync_GivenAnItemPastItsExpiry_RefusesIt()
    {
        // arrange: DynamoDB reaps expired items on its own schedule, typically within 48 hours, so
        // an item still being present is not evidence that it is still live.
        GivenTheStoredItem(("Email", new AttributeValue { S = "ben@example.com" }), Expiring(inMinutes: -1));

        // act
        var (redeemed, email) = await _sut.TryRedeemLoginTokenAsync("a-token");

        // assert
        redeemed.Should().BeFalse();
        email.Should().BeEmpty();
    }

    [Fact]
    public async Task TryRedeemLoginTokenAsync_GivenAnAlreadyRedeemedToken_RefusesIt()
    {
        // arrange: the conditional delete failing is how "unknown, already used, or reaped" arrives.
        _dynamoDb.DeleteItemAsync(Arg.Any<DeleteItemRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConditionalCheckFailedException("nope"));

        // act
        var (redeemed, email) = await _sut.TryRedeemLoginTokenAsync("a-token");

        // assert
        redeemed.Should().BeFalse();
        email.Should().BeEmpty();
    }

    [Fact]
    public async Task TryReserveRequestSlotAsync_GivenNoRecentRequest_TakesTheSlot()
    {
        // act
        var reserved = await _sut.TryReserveRequestSlotAsync("ben@example.com");

        // assert
        reserved.Should().BeTrue();
    }

    [Fact]
    public async Task TryReserveRequestSlotAsync_GivenARequestInsideTheWindow_RefusesTheSlot()
    {
        // arrange
        _dynamoDb.PutItemAsync(Arg.Any<PutItemRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConditionalCheckFailedException("nope"));

        // act
        var reserved = await _sut.TryReserveRequestSlotAsync("ben@example.com");

        // assert: without this the endpoint is an open relay pointed at arbitrary addresses, which
        // is a deliverability problem before it is a security one.
        reserved.Should().BeFalse();
    }

    [Fact]
    public async Task TryReserveRequestSlotAsync_KeysTheThrottleOnTheNormalizedAddress()
    {
        // act: otherwise the same person is throttled separately per spelling of their address.
        await _sut.TryReserveRequestSlotAsync("  Ben@Example.COM ");

        // assert
        CapturedPut().Item["PK"].S.Should().Be("LOGINTHROTTLE#ben@example.com");
    }

    [Fact]
    public async Task TryReserveRequestSlotAsync_KeysBothThrottlesOnTheInboxRatherThanTheSpelling()
    {
        // act: a +tag reaches the same inbox, so it must not buy that inbox another day of links.
        await _sut.TryReserveRequestSlotAsync("Ben+hat@Example.com");

        // assert
        CapturedPut().Item["PK"].S.Should().Be("LOGINTHROTTLE#ben@example.com");
        CapturedUpdate().Key["PK"].S.Should().Be($"LOGINDAY#ben@example.com#{DateTimeOffset.UtcNow.UtcDateTime:yyyy-MM-dd}");
    }

    [Fact]
    public async Task TryReserveRequestSlotAsync_CountsAgainstTheDayOnlyUpToItsLimit()
    {
        // act
        await _sut.TryReserveRequestSlotAsync("ben@example.com");

        // assert: the check and the count are one conditional write, so two requests cannot both
        // take the last slot.
        var update = CapturedUpdate();

        update.UpdateExpression.Should().Contain("ADD RequestCount :one");
        update.ConditionExpression.Should().Be("attribute_not_exists(RequestCount) OR RequestCount < :limit");
        update.ExpressionAttributeValues[":limit"].N.Should().Be(LoginTokenProvider.DailyLinkLimit.ToString());
    }

    [Fact]
    public async Task TryReserveRequestSlotAsync_GivenADayThatHasHadItsFill_RefusesTheSlot()
    {
        // arrange
        _dynamoDb.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConditionalCheckFailedException("nope"));

        // act
        var reserved = await _sut.TryReserveRequestSlotAsync("ben@example.com");

        // assert
        reserved.Should().BeFalse();
    }

    [Fact]
    public async Task TryReserveRequestSlotAsync_GivenARequestInsideTheMinute_DoesNotSpendOneOfTheDays()
    {
        // arrange
        _dynamoDb.PutItemAsync(Arg.Any<PutItemRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConditionalCheckFailedException("nope"));

        // act
        await _sut.TryReserveRequestSlotAsync("ben@example.com");

        // assert
        await _dynamoDb.DidNotReceiveWithAnyArgs().UpdateItemAsync(default(UpdateItemRequest)!, default);
    }

    private void GivenTheStoredItem(params (string Name, AttributeValue Value)[] attributes) =>
        _dynamoDb.DeleteItemAsync(Arg.Any<DeleteItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteItemResponse
            {
                Attributes = attributes.ToDictionary(pair => pair.Name, pair => pair.Value)
            });

    private static (string, AttributeValue) Expiring(int inMinutes) =>
        ("ExpiresAt", new AttributeValue
        {
            N = DateTimeOffset.UtcNow.AddMinutes(inMinutes).ToUnixTimeSeconds().ToString()
        });

    private PutItemRequest CapturedPut() =>
        _dynamoDb.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<PutItemRequest>()
            .Last();

    private UpdateItemRequest CapturedUpdate() =>
        _dynamoDb.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<UpdateItemRequest>()
            .Last();

    private DeleteItemRequest CapturedDelete() =>
        _dynamoDb.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<DeleteItemRequest>()
            .Last();

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
