namespace GiftExchange.Library.Abstractions;

/// <summary>
/// A request made by somebody about their own place in a gift exchange.
/// <see cref="Utility.ApiGatewayAdapter"/> overwrites <see cref="ParticipantEmail"/> with the
/// authenticated caller before the request reaches a service, so a client cannot act as another
/// participant by naming them.
/// </summary>
/// <remarks>
/// The participant's counterpart of <see cref="IOrganizerScopedRequest"/>, and a separate interface
/// rather than a reuse of it. The two find an exchange in different ways — by who owns it, and by
/// who is in it — and a request that could be read either way is one wrong guess away from acting
/// on an exchange that is not the caller's.
/// </remarks>
internal interface IParticipantScopedRequest
{
    string ParticipantEmail { get; init; }

    IParticipantScopedRequest WithParticipantEmail(string participantEmail);
}
