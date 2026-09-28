namespace GiftExchange.Library.Abstractions;

/// <summary>
/// Whether SES will refuse to deliver to an address, because the account has it on its suppression
/// list.
/// </summary>
/// <remarks>
/// An interface for the reason <see cref="IReplyThrottleProvider"/> is one: the implementation talks
/// to AWS, and the tests that add participants should not.
/// </remarks>
internal interface ISuppressionListProvider
{
    /// <summary>
    /// True when mail to this address would be dropped by SES before it left.
    /// </summary>
    Task<bool> IsSuppressedAsync(string email);
}
