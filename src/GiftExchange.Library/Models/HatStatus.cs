namespace GiftExchange.Library.Models;

public static class HatStatus
{
    public static string InProgress => "IN_PROGRESS";

    /// <summary>
    /// The gift exchange has been validated and is ready for the assignment of gift recipients. The exchange remains editable, but edits may invalidate the readiness for assignment.
    /// </summary>
    public static string ReadyForAssignment => "READY_FOR_ASSIGNMENT";

    /// <summary>
    /// The names of gift exchange participants have been assigned to each participant as their gift recipient.
    /// </summary>
    public static string NamesAssigned => "NAMES_ASSIGNED";

    /// <summary>
    /// Invitations have been sent to all participants. Technically, the invitations have been queued for sending, but this status indicates that the process is complete.
    /// </summary>
    public static string InvitationsSent => "INVITATIONS_SENT";

    /// <summary>
    /// Invitations have been sent and the gift exchange is in a waiting period before it can be closed.
    /// </summary>
    public static string CoolingOff => "COOLING_OFF";

    /// <summary>
    /// The owner of the gift exchange indicated that the gift exchange has concluded. The names of picked recipients can be revealed, and no further changes can be made.
    /// </summary>
    public static string Closed => "CLOSED";
}

public static class HatStatuses
{
    public static readonly ImmutableList<string> All = [
        HatStatus.InProgress,
        HatStatus.ReadyForAssignment,
        HatStatus.NamesAssigned,
        HatStatus.InvitationsSent,
        HatStatus.CoolingOff,
        HatStatus.Closed
    ];
}
