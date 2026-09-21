using Amazon.DynamoDBv2.Model;

namespace GiftExchange.Library.Providers;

/// <summary>
/// Limits on how often a repeated request can make this application send mail.
/// </summary>
/// <remarks>
/// Three limits, all held per participant: how often one participant can Ask another for gift ideas,
/// how often one can offer ideas about another unprompted, and how often an organizer can correct
/// one participant's address and resend.
///
/// The same conditional-put-with-TTL arrangement <c>LoginTokenProvider</c> uses to throttle magic
/// link requests, against the same table.
/// </remarks>
[UsedImplicitly]
internal class ReplyThrottleProvider : IReplyThrottleProvider
{
    private readonly IAmazonDynamoDB _dynamoDbClient;

    private readonly ILogger<ReplyThrottleProvider> _logger;

    private readonly string _tableName;

    // ReSharper disable once ConvertToPrimaryConstructor
    public ReplyThrottleProvider(IAmazonDynamoDB dynamoDbClient, ILogger<ReplyThrottleProvider> logger)
    {
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tableName = EnvReader.GetStringValue("TABLE_NAME");
    }

    /// <inheritdoc />
    public Task<ReserveSlotResponse> TryReserveAskSlotAsync(ReserveAskSlotRequest request) =>
        ReserveAsync(
            // Both ids, so that asking a second person is a separate slot from asking the first.
            $"ASKTHROTTLE#{request.AskerParticipantId}#{request.TargetParticipantId}",
            "ASKTHROTTLE",
            request.Window,
            "an Ask");

    /// <inheritdoc />
    public Task<ReserveSlotResponse> TryReserveOfferSlotAsync(
        ReserveOfferSlotRequest request
    ) =>
        ReserveAsync(
            // A prefix of its own, so that being asked about somebody and volunteering about them
            // cannot suppress one another.
            $"OFFERTHROTTLE#{request.SharerParticipantId}#{request.SubjectParticipantId}",
            "OFFERTHROTTLE",
            request.Window,
            "an offer of gift ideas");

    /// <inheritdoc />
    public Task<ReserveSlotResponse> TryReserveAddressChangeSlotAsync(
        ReserveAddressChangeSlotRequest request
    ) =>
        ReserveAsync(
            $"ADDRESSCHANGETHROTTLE#{request.ParticipantId}",
            "ADDRESSCHANGETHROTTLE",
            request.Window,
            "an address change");

    /// <summary>
    /// The conditional put both slot methods are: write an item that expires, unless one is already
    /// there and has not.
    /// </summary>
    /// <remarks>
    /// One implementation rather than two, now that both speak the same request and answer with the
    /// same response. Everything that differed between them was the key, which is the only thing
    /// still passed in.
    /// </remarks>
    /// <param name="what">Names the thing being suppressed, for the log line when this fails.</param>
    private async Task<ReserveSlotResponse> ReserveAsync(
        string partitionKey,
        string sortKey,
        TimeSpan window,
        string what
    )
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(window).ToUnixTimeSeconds();

        var request = new PutItemRequest
        {
            TableName = _tableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = partitionKey },
                ["SK"] = new() { S = sortKey },
                // Still AskedAt, though this now also holds address changes. It is the attribute
                // name already written into live items, and renaming it would silently lose the
                // timestamp on every one still inside its window -- for no gain, since nothing
                // reads it but ReadAskedAt.
                ["AskedAt"] = new() { N = now.ToUnixTimeSeconds().ToString() },
                ["ExpiresAt"] = new() { N = expiresAt.ToString() },
                ["ttl"] = new() { N = expiresAt.ToString() }
            },
            ConditionExpression = "attribute_not_exists(PK) OR ExpiresAt < :now",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":now"] = new() { N = now.ToUnixTimeSeconds().ToString() }
            },
            // The item that blocked the write comes back with the rejection, which is the only way
            // to tell the caller when the last one was without a second read that could disagree
            // with the one that just refused them.
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.ALL_OLD
        };

        try
        {
            await _dynamoDbClient.PutItemAsync(request).ConfigureAwait(false);
            return ReserveSlotResponses.Reserved;
        }
        catch (ConditionalCheckFailedException exception)
        {
            return ReserveSlotResponses.RefusedSince(ReadAskedAt(exception));
        }
        catch (Exception exception)
        {
            // Fails closed, unlike most throttles. An Ask not sent is a nuisance and an
            // organizer waiting a few minutes is an inconvenience; an outage that lifts either
            // limit is a way to send unmetered mail through this application.
            _logger.LogError(exception, "Could not reserve a slot; suppressing {What}.", what);
            return ReserveSlotResponses.Refused;
        }
    }

    /// <summary>
    /// When the Ask that blocked this one was made, or the minimum if the item did not come back.
    /// </summary>
    /// <remarks>
    /// Tolerant on purpose. The date only shapes a sentence in an email, so a missing or unparseable
    /// value is worth degrading over rather than failing over — the caller words it differently and
    /// the throttle still holds.
    /// </remarks>
    private static DateTimeOffset ReadAskedAt(ConditionalCheckFailedException exception) =>
        exception.Item is not null
        && exception.Item.TryGetValue("AskedAt", out var askedAt)
        && long.TryParse(askedAt.N, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.MinValue;
}
