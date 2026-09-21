namespace GiftExchange.Library.Models;

public record Participant
{
    /// <summary>
    /// Who this participant drew, or <see cref="Persons.Empty"/> when they have not drawn anybody.
    /// </summary>
    /// <remarks>
    /// A person rather than a name, because names are not unique within an exchange — two people
    /// called Sam are two people. The address is what identifies them; the name is what is shown.
    /// </remarks>
    public required Person PickedRecipient { get; init; }

    public required Person Person { get; init; }

    /// <summary>
    /// Who this participant may draw. People rather than names, for the reason
    /// <see cref="PickedRecipient"/> is one.
    /// </summary>
    public required ImmutableList<Person> EligibleRecipients { get; init; }

    /// <summary>
    /// The face this participant is marked with wherever they are named — one of
    /// <c>PersonEmoji.All</c>, and empty only for a record that stands for nobody.
    /// </summary>
    /// <remarks>
    /// Stored rather than derived, so an organizer can change it and so it says the same thing in
    /// the list, in the invitation and in the announcement. It is decoration and nothing else:
    /// nothing is identified by it, and two people in one hat may end up sharing one.
    /// </remarks>
    public required string Emoji { get; init; }

    /// <summary>
    /// How far the last email this exchange sent them is known to have got — one of
    /// <see cref="Models.DeliveryStatus"/>, and empty until something is heard.
    /// </summary>
    /// <remarks>
    /// Empty is the state before anything is sent and for a while after it: SES publishes events
    /// asynchronously, so a participant sits here for seconds even when everything works. It means
    /// "nothing heard", and never "not delivered". Anything showing this to an organizer has to say
    /// so, because the two readings lead to opposite actions — one is a reason to check an address,
    /// the other a reason to wait.
    /// </remarks>
    public required string DeliveryStatus { get; init; }

    /// <summary>
    /// Why, when <see cref="DeliveryStatus"/> is one of the ones with a why: the bounce subtype and
    /// what the receiving server said. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// Written by a stranger's mail server rather than by this application or by an organizer, so
    /// it is the one field on this record that has passed through no moderation at all. Whatever
    /// renders it encodes it.
    /// </remarks>
    public required string DeliveryDetail { get; init; }

    /// <summary>
    /// Which of this application's emails <see cref="DeliveryStatus"/> is about — one of
    /// <see cref="EmailMessageType"/>, and empty while nothing has been heard.
    /// </summary>
    /// <remarks>
    /// An exchange sends more than one thing to the same person, and the status here is the newest
    /// row rather than the invitation's. Without this an organizer reading "delivered" next to
    /// somebody who says they never saw the invitation has no way to tell whether that word is
    /// about the invitation at all — it may be the announcement that the exchange had finished,
    /// sent weeks later to an address that swallowed the first message.
    /// </remarks>
    public required string DeliveryMessageType { get; init; }

    /// <summary>
    /// When SES says the event behind <see cref="DeliveryStatus"/> happened.
    /// <see cref="DateTimeOffset.MinValue"/> while nothing has been heard, which is how the rest of
    /// this API spells a timestamp it does not have.
    /// </summary>
    /// <remarks>
    /// This is the fact an organizer can hand to a participant who cannot find their invitation.
    /// "Delivered" alone gives them nothing to search for; a date and a time tells them which
    /// morning to look in, which is the difference between finding it in a junk folder and
    /// concluding it was never sent.
    /// </remarks>
    public required DateTimeOffset DeliveryOccurredAt { get; init; }
}

internal static class Participants
{
    public static Participant Empty => new()
    {
        Person = Persons.Empty,
        PickedRecipient = Persons.Empty,
        EligibleRecipients = [],
        Emoji = string.Empty,
        DeliveryStatus = Models.DeliveryStatus.Unknown,
        DeliveryDetail = string.Empty,
        DeliveryMessageType = string.Empty,
        DeliveryOccurredAt = DateTimeOffset.MinValue
    };
}
