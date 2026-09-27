namespace GiftExchange.Library.Messaging;

/// <summary>
/// Everything that varies between one participant's invitation and the next.
/// </summary>
/// <remarks>
/// A record rather than five positional parameters, because four of the five are strings and three
/// of those are names or tokens. Nothing but the argument order stopped a caller from handing the
/// picked name where the participant's belonged — which would tell somebody they had drawn
/// themselves — or the leave token where the gift ideas one belonged, which would put a working
/// removal link behind a button labelled "share gift ideas".
///
/// The hat carries the organizer, the exchange name, the price range and the additional
/// information, so everything an invitation says that is the same for everybody comes from there.
/// </remarks>
[UsedImplicitly]
internal record ComposeInvitationRequest
{
    public required Hat Hat { get; init; }

    /// <summary>Who is being written to. Appears in the greeting and nowhere else.</summary>
    public required string ParticipantName { get; init; }

    /// <summary>
    /// Whose name they drew — the one secret this email exists to deliver, and the one thing in it
    /// that must never reach anybody else.
    /// </summary>
    public required string PickedName { get; init; }

    /// <summary>
    /// The face that person is marked with in this hat, so the invitation and the announcement at
    /// the end of the exchange mark them the same way.
    /// </summary>
    /// <remarks>
    /// Carried rather than looked up inside the composer, so a preview -- which names nobody real
    /// -- can supply a stand-in instead of the composer having to know that some invitations are
    /// about imaginary people.
    /// </remarks>
    public required string PickedEmoji { get; init; }

    /// <summary>
    /// Their gift ideas routing token, or empty. Empty leaves the ask and share blocks out entirely
    /// rather than rendering an address that routes nowhere.
    /// </summary>
    public required string GiftIdeasToken { get; init; }

    /// <summary>
    /// Their leave token, or empty. Empty for the organizer, who is never issued one, and on a
    /// preview of an invitation that has not been sent — in both cases the leave sentence is left
    /// out of the fine print rather than pointed nowhere.
    /// </summary>
    public required string LeaveToken { get; init; }

    /// <summary>
    /// Whether the wordmark leads the invitation. False on the site's copy of it, whose page shell
    /// already carries one.
    /// </summary>
    public bool IncludeMasthead { get; init; } = true;
}
