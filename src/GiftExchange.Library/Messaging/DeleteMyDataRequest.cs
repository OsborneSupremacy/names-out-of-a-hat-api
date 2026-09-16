namespace GiftExchange.Library.Messaging;

/// <summary>
/// A request, from the signed-in person, to delete everything they have organized.
/// </summary>
[UsedImplicitly]
public record DeleteMyDataRequest : IOrganizerScopedRequest
{
    /// <summary>
    /// Filled in by <see cref="Utility.ApiGatewayAdapter"/> from the authorizer. Clients do not
    /// send it, and whose data is deleted is never something the body can say.
    /// </summary>
    public string OrganizerEmail { get; init; } = string.Empty;

    /// <summary>Also remove the person themselves, where nothing else still refers to them.</summary>
    public required bool ForgetMe { get; init; }

    /// <summary>Never let anybody add this address to a gift exchange again.</summary>
    public required bool DoNotAddAnywhere { get; init; }

    IOrganizerScopedRequest IOrganizerScopedRequest.WithOrganizerEmail(string organizerEmail) =>
        this with { OrganizerEmail = organizerEmail };
}
