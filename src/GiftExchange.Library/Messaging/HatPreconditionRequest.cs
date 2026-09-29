namespace GiftExchange.Library.Messaging;

internal record HatPreconditionRequest
{
    public required Guid HatId { get; init; }

    public required string OrganizerEmail { get; init; }

    public required Dictionary<string, string> FieldsToModerate { get; init; }

    /// <summary>
    /// What each field in <see cref="FieldsToModerate"/> holds already, read from the exchange once
    /// it has been loaded. A field whose value is unchanged is not moderated again: it was checked
    /// when it was saved, and checking it on every edit made each save cost a Comprehend call per
    /// field whether anything in it had changed or not.
    /// </summary>
    /// <remarks>
    /// Keys missing from the result are moderated, so a caller that supplies nothing moderates
    /// everything, as before.
    /// </remarks>
    public Func<Hat, IReadOnlyDictionary<string, string>> StoredValues { get; init; } =
        _ => ImmutableDictionary<string, string>.Empty;

    public required ImmutableList<string> ValidHatStatuses { get; init; }
}
