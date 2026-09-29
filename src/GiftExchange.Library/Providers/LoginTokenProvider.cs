using Amazon.DynamoDBv2.Model;

namespace GiftExchange.Library.Providers;

/// <summary>
/// Storage for single-use magic link tokens. Only the hash of a token is ever persisted, so a
/// dump of the table does not let an attacker redeem pending links.
/// </summary>
[UsedImplicitly]
internal class LoginTokenProvider
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The most sign-in links one inbox is sent in a UTC day.
    /// </summary>
    /// <remarks>
    /// The per-minute throttle alone still let one inbox be sent 1,440 links a day, from our domain,
    /// by anybody who knew the address -- and every one that was reported as spam counted against
    /// the SES account that invitations share. Nobody signing in for themselves asks ten times in a
    /// day.
    ///
    /// The cost is the other way round: somebody who wanted to could use up an inbox's ten and keep
    /// its owner out until midnight UTC. That is a nuisance to one person, where the flood was a
    /// nuisance to them and a risk to everybody's mail, and it is the trade this makes.
    /// </remarks>
    internal const int DailyLinkLimit = 10;

    private readonly IAmazonDynamoDB _dynamoDbClient;

    private readonly string _tableName;

    // ReSharper disable once ConvertToPrimaryConstructor
    public LoginTokenProvider(IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
        _tableName = EnvReader.GetStringValue("TABLE_NAME");
    }

    /// <summary>
    /// Issues a login token for the supplied address, storing only its hash.
    /// </summary>
    /// <returns>The plaintext token. This is the only time it exists outside the email.</returns>
    public async Task<string> CreateLoginTokenAsync(string email)
    {
        var token = SecretToken.Create(SecretToken.OpaqueTokenBytes);
        var expiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);

        var request = new PutItemRequest
        {
            TableName = _tableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = BuildLoginKey(token) },
                ["SK"] = new() { S = "LOGIN" },
                ["Email"] = new() { S = NormalizeEmail(email) },
                ["ExpiresAt"] = new() { N = expiresAt.ToUnixTimeSeconds().ToString() },
                ["ttl"] = new() { N = expiresAt.ToUnixTimeSeconds().ToString() }
            }
        };

        await _dynamoDbClient.PutItemAsync(request).ConfigureAwait(false);

        return token;
    }

    /// <summary>
    /// Atomically consumes a login token. The conditional delete is what makes a token single-use:
    /// two concurrent redemptions cannot both succeed.
    /// </summary>
    public async Task<(bool redeemed, string email)> TryRedeemLoginTokenAsync(string token)
    {
        var request = new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = BuildLoginKey(token) },
                ["SK"] = new() { S = "LOGIN" }
            },
            ConditionExpression = "attribute_exists(PK)",
            ReturnValues = ReturnValue.ALL_OLD
        };

        try
        {
            var response = await _dynamoDbClient
                .DeleteItemAsync(request)
                .ConfigureAwait(false);

            // DynamoDB deletes expired items on its own schedule (typically within 48 hours), so an
            // item being present is not proof that it is still live. Check the expiry ourselves.
            if (!response.Attributes.TryGetValue("ExpiresAt", out var expiresAt)
                || long.Parse(expiresAt.N) < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return (false, string.Empty);

            return (true, response.Attributes["Email"].S);
        }
        catch (ConditionalCheckFailedException)
        {
            // Unknown, already redeemed, or reaped by TTL.
            return (false, string.Empty);
        }
    }

    /// <summary>
    /// Per-inbox throttle for link requests: one a minute, and <see cref="DailyLinkLimit"/> a day.
    /// Without this the endpoint is an open email relay pointed at arbitrary addresses, which is a
    /// deliverability and billing problem before it is a security one.
    /// </summary>
    /// <remarks>
    /// Keyed by <c>ToMailboxKey</c> rather than by spelling, because both limits are about how
    /// much mail lands in one inbox, and <c>me+1@</c>, <c>me+2@</c> and so on all land in the same
    /// one.
    ///
    /// The minute is checked first so that a request refused by it does not also spend one of the
    /// day's links.
    /// </remarks>
    /// <returns>false when this inbox may not be sent another link yet.</returns>
    public async Task<bool> TryReserveRequestSlotAsync(string email)
    {
        var mailbox = email.ToMailboxKey();

        return await TryReserveMinuteSlotAsync(mailbox).ConfigureAwait(false)
               && await TryReserveDailySlotAsync(mailbox).ConfigureAwait(false);
    }

    private async Task<bool> TryReserveMinuteSlotAsync(string mailbox)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiresAt = DateTimeOffset.UtcNow.Add(ThrottleWindow).ToUnixTimeSeconds();

        var request = new PutItemRequest
        {
            TableName = _tableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"LOGINTHROTTLE#{mailbox}" },
                ["SK"] = new() { S = "LOGINTHROTTLE" },
                ["ExpiresAt"] = new() { N = expiresAt.ToString() },
                ["ttl"] = new() { N = expiresAt.ToString() }
            },
            ConditionExpression = "attribute_not_exists(PK) OR ExpiresAt < :now",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":now"] = new() { N = now.ToString() }
            }
        };

        try
        {
            await _dynamoDbClient.PutItemAsync(request).ConfigureAwait(false);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Counts one more link against the inbox's UTC day, unless the day has had its fill.
    /// </summary>
    /// <remarks>
    /// One item per inbox per day, with the date in its key, so a new day is a new item and never
    /// needs resetting. The count and the check are one conditional update, so two requests racing
    /// cannot both take the last slot.
    /// </remarks>
    private async Task<bool> TryReserveDailySlotAsync(string mailbox)
    {
        var today = DateTimeOffset.UtcNow.UtcDateTime.Date;

        // A day past the day it counts, so an item is never reaped while it can still refuse.
        var expiresAt = new DateTimeOffset(today.AddDays(2), TimeSpan.Zero).ToUnixTimeSeconds();

        var request = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"LOGINDAY#{mailbox}#{today:yyyy-MM-dd}" },
                ["SK"] = new() { S = "LOGINDAY" }
            },
            UpdateExpression = "ADD RequestCount :one SET #ttl = if_not_exists(#ttl, :ttl)",
            ConditionExpression = "attribute_not_exists(RequestCount) OR RequestCount < :limit",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#ttl"] = "ttl"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":one"] = new() { N = "1" },
                [":limit"] = new() { N = DailyLinkLimit.ToString() },
                [":ttl"] = new() { N = expiresAt.ToString() }
            }
        };

        try
        {
            await _dynamoDbClient.UpdateItemAsync(request).ConfigureAwait(false);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The item key a token is stored under: its digest, never the token.
    /// </summary>
    /// <remarks>
    /// The prefix is what keeps login items apart from the throttle items sharing this table. The
    /// hashing itself is <see cref="SecretToken"/>'s, so this class and the gift ideas routing
    /// tokens cannot drift into two different ideas of what "stored as a hash" means.
    /// </remarks>
    private static string BuildLoginKey(string token) =>
        $"LOGIN#{SecretToken.Hash(token)}";

    private static string NormalizeEmail(string email) =>
        email.TrimNullSafe().ToLowerInvariant();
}
