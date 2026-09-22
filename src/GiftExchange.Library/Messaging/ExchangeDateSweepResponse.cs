namespace GiftExchange.Library.Messaging;

/// <summary>What one run of the daily sweep did, for its log line and for tests.</summary>
internal record ExchangeDateSweepResponse
{
    public required int ClosePromptsSent { get; init; }

    public required int HatsPurged { get; init; }
}
