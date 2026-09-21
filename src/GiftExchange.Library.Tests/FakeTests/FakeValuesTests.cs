using GiftExchange.Library.Validators;

namespace GiftExchange.Library.Tests.FakeTests;

/// <summary>
/// The fakers claim to produce data the application would actually accept, and to produce enough
/// of it that two subjects never collide. Nothing held them to either claim, and both were being
/// broken.
///
/// The collision is the one worth a test rather than a comment. Names were drawn straight from
/// Bogus, whose first-name pool is small enough that a hat seeded with a handful of participants
/// regularly got two of the same. Participants within a gift exchange used to need distinct names,
/// so the duplicate never failed where it was made; it surfaced somewhere else entirely, as whatever
/// that test happened to be asserting, in about one full run in five.
///
/// Duplicates are allowed now, and the guarantee is still worth keeping. Two participants sharing a
/// name are named with their address in brackets, so a test looking for a bare name in an email
/// would fail on the one run in five where the faker happened to repeat itself.
/// </summary>
public class FakeValuesTests
{
    /// <summary>
    /// A hat's worth, because that is the largest batch any test asks for and so the worst case
    /// the fakers have to survive.
    /// </summary>
    private const int AHatsWorth = 50;

    /// <summary>
    /// Lower-cased before comparing, because that is how <c>ParticipantNaming</c> compares them: two
    /// names this test called distinct and that one calls equal would leave the flake exactly where
    /// it was.
    /// </summary>
    [Fact]
    public void FakedParticipants_InOneHatsWorth_AllHaveDistinctNames()
    {
        var names = new AddParticipantRequestFaker()
            .Generate(AHatsWorth)
            .Select(request => request.Name.ToLowerInvariant());

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void FakedPeople_InOneHatsWorth_AllHaveDistinctNames()
    {
        var names = new PersonFaker()
            .Generate(AHatsWorth)
            .Select(person => person.Name.ToLowerInvariant());

        names.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// An organizer is a participant of their own exchange, so their name shares the one namespace
    /// every other name in that hat is in.
    /// </summary>
    [Fact]
    public void FakedOrganizers_AreDistinctFromFakedParticipants()
    {
        var organizers = new HatDataModelFaker()
            .Generate(AHatsWorth)
            .Select(hat => hat.OrganizerName.ToLowerInvariant());

        var participants = new AddParticipantRequestFaker()
            .Generate(AHatsWorth)
            .Select(request => request.Name.ToLowerInvariant());

        organizers.Concat(participants).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// A person is one row for the whole application now, so two faked subjects sharing an address
    /// would be one subject wearing two tests' expectations.
    /// </summary>
    [Fact]
    public void FakedSubjects_AcrossEveryFaker_HaveDistinctAddresses()
    {
        var addresses = new AddParticipantRequestFaker().Generate(AHatsWorth).Select(request => request.Email)
            .Concat(new HatDataModelFaker().Generate(AHatsWorth).Select(hat => hat.OrganizerEmail))
            .Concat(new CreateHatRequestFaker().Generate(AHatsWorth).Select(request => request.OrganizerEmail))
            .Concat(new PersonFaker().Generate(AHatsWorth).Select(person => person.Email))
            .Select(address => address.ToLowerInvariant());

        addresses.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// The other half of the claim in FakeValues' summary. A faker that generated data the
    /// application would refuse would let a handler test pass on input no client could ever send —
    /// and these tests mostly bypass the validators, so nothing else would notice.
    /// </summary>
    [Fact]
    public void EveryFakedParticipant_IsSomethingTheApplicationWouldAccept()
    {
        var validator = new AddParticipantRequestValidator();

        foreach (var request in new AddParticipantRequestFaker().Generate(AHatsWorth))
            validator.Validate(request).Errors
                .Should().BeEmpty($"the faker produced `{request.Name}` <{request.Email}>");
    }

    [Fact]
    public void EveryFakedHat_IsSomethingTheApplicationWouldAccept()
    {
        var validator = new CreateHatRequestValidator();

        foreach (var request in new CreateHatRequestFaker().Generate(AHatsWorth))
            validator.Validate(request).Errors
                .Should().BeEmpty($"the faker produced `{request.HatName}` for `{request.OrganizerName}`");
    }
}
