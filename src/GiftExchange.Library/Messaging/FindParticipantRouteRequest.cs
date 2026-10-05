namespace GiftExchange.Library.Messaging;

/// <summary>
/// Finds a participant by who they are and which exchange they are in, rather than by a token from
/// an email.
/// </summary>
/// <remarks>
/// The signed-in counterpart of a token hash. A token is the whole credential on the pages an email
/// links to; here the credential is the session, and the participant is whichever row that session's
/// address holds in the exchange named.
/// </remarks>
internal record FindParticipantRouteRequest
{
    /// <summary>The authenticated caller. Never read from anything the client sent.</summary>
    public required string ParticipantEmail { get; init; }

    public required Guid HatId { get; init; }
}
