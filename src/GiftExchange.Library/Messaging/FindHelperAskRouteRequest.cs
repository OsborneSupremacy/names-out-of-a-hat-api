namespace GiftExchange.Library.Messaging;

/// <summary>
/// Finds an ask for gift ideas by its id, for the person it was put to.
/// </summary>
/// <remarks>
/// The signed-in counterpart of the token in the email the ask arrived in. The id comes from the
/// client and authorises nothing on its own: it resolves only if the caller is the helper the ask
/// was put to, in the exchange named.
/// </remarks>
internal record FindHelperAskRouteRequest
{
    /// <summary>The authenticated caller. Never read from anything the client sent.</summary>
    public required string HelperEmail { get; init; }

    public required Guid HatId { get; init; }

    public required Guid AskId { get; init; }
}
