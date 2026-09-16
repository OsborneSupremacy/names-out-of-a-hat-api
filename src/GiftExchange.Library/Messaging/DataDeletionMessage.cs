namespace GiftExchange.Library.Messaging;

/// <summary>
/// What <see cref="Services.DeleteMyDataService"/> puts on the data deletion queue for
/// <see cref="Services.DataDeletionQueueHandlerService"/> to carry out.
/// </summary>
[UsedImplicitly]
internal record DataDeletionMessage
{
    /// <summary>The address whose data is being deleted, as the authorizer established it.</summary>
    public required string Email { get; init; }

    /// <summary>Also remove the person themselves, where nothing else still refers to them.</summary>
    public required bool ForgetMe { get; init; }

    /// <summary>
    /// When they asked. Exchanges created after this are not deleted: the message may wait on the
    /// queue, and somebody who starts a new exchange in the meantime did not ask to lose it.
    /// </summary>
    public required DateTimeOffset RequestedAt { get; init; }
}
