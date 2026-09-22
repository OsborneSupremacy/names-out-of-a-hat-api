namespace GiftExchange.Library.Messaging;

/// <summary>
/// What EventBridge Scheduler hands the daily sweep. Nothing: the sweep works out what is due from
/// the clock and the database.
/// </summary>
/// <remarks>
/// A record rather than no parameter at all, because the Lambda runtime has to deserialize the
/// event into something, and a type of our own is what the source-generated serializer context
/// can be told about.
/// </remarks>
public record ExchangeDateSweepRequest;
