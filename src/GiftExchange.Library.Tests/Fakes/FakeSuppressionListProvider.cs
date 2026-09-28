namespace GiftExchange.Library.Tests.Fakes;

/// <summary>
/// Suppresses exactly the addresses it is given, compared the way SES would: case aside.
/// </summary>
internal class FakeSuppressionListProvider : ISuppressionListProvider
{
    private readonly HashSet<string> _suppressed = new(StringComparer.OrdinalIgnoreCase);

    public void Suppress(string email) => _suppressed.Add(email.Trim());

    public Task<bool> IsSuppressedAsync(string email) =>
        Task.FromResult(_suppressed.Contains(email.Trim()));
}
