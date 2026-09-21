namespace GiftExchange.Library.Services;

/// <summary>
/// How one participant is named to somebody else in the same exchange.
/// </summary>
/// <remarks>
/// Names are not unique within an exchange. A person is one row shared by every exchange they are
/// in, so a rule that two participants may not share a name reached well past the exchange being
/// edited — a rename could be refused over a stranger in somebody else's exchange. What an exchange
/// actually cannot hold is the same person twice, and the address is what says who that is.
///
/// So when two people in one exchange answer to the same name, anything naming one of them to
/// somebody else adds the address: "Sam (sam@example.com)". Only then — the address is somebody's
/// contact detail, and there is no reason to hand it round when the name alone already says who.
///
/// Display only. <see cref="Person.Name"/> stays the bare name everywhere, because the checks that
/// look for a recipient's name in what a giver wrote are looking for what the giver would type.
/// </remarks>
internal static class ParticipantNaming
{
    /// <summary>
    /// The name, followed by the address in brackets when another of <paramref name="hatmates"/>
    /// goes by the same name. Not encoded; callers writing HTML encode it as they would a name.
    /// </summary>
    internal static string DisplayName(Person person, IEnumerable<Person> hatmates)
    {
        if (string.IsNullOrWhiteSpace(person.Name))
            return person.Name;

        var shared = hatmates.Any(other =>
            !other.Email.ContentEquals(person.Email)
            && other.Name.ContentEquals(person.Name));

        return shared ? $"{person.Name} ({person.Email})" : person.Name;
    }

    /// <summary>
    /// <see cref="DisplayName(Person, IEnumerable{Person})"/> against everybody in this hat.
    /// </summary>
    internal static string DisplayNameIn(this Hat hat, Person person) =>
        DisplayName(person, hat.Participants.Select(participant => participant.Person));

    /// <summary>
    /// <see cref="DisplayName(Person, IEnumerable{Person})"/> against everybody in the exchange a
    /// gift ideas link belongs to.
    /// </summary>
    internal static string DisplayNameOf(this GiftIdeaRoute route, Person person) =>
        DisplayName(person, route.Hatmates);
}
