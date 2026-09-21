namespace GiftExchange.Library.Messaging;

/// <summary>
/// What became of an attempt to change the name somebody goes by.
/// </summary>
internal record RenamePersonResponse
{
    public required NameChangeOutcome Outcome { get; init; }

    /// <summary>The person that was renamed, or the all-zero id when nobody was.</summary>
    public required Guid PersonId { get; init; }

    /// <summary>
    /// The name they went by before, or the empty string when nothing was written. Worth returning
    /// so that a refusal can quote who is being talked about, and a log line can say what actually
    /// changed rather than only that something did.
    /// </summary>
    public required string PreviousName { get; init; }
}

internal static class RenamePersonResponses
{
    /// <summary>Nobody was renamed. The shape every failure starts from.</summary>
    public static RenamePersonResponse For(NameChangeOutcome outcome) =>
        new()
        {
            Outcome = outcome,
            PersonId = Guid.Empty,
            PreviousName = string.Empty
        };
}
