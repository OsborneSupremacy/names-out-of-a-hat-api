using System.Text.RegularExpressions;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The email that goes out once the organizer says the exchange has happened. It is the only one
/// that names every pick, so what it says about them is worth pinning down.
/// </summary>
public class CompletionEmailCompositionServiceTests
{
    private readonly CompletionEmailCompositionService _sut = new();

    [Theory]
    [InlineData("Family Christmas", "The gift exchange, Family Christmas, has finished")]
    // The same reason the invitation subject reads the way it does: the sentence never leans on
    // the organizer's name for the exchange, so any name slots in.
    [InlineData("Christmas On August 27", "The gift exchange, Christmas On August 27, has finished")]
    public void GetSubject_NamesTheExchangeWithoutBuildingTheSentenceAroundItsName(
        string hatName,
        string expected)
    {
        // act
        var subject = CompletionEmailCompositionService.GetSubject(HatFor(hatName));

        // assert
        subject.Should().Be(expected);
    }

    [Fact]
    public void GetSubject_GivenNoName_StillReads()
    {
        // act: the validators require a name, so this is the defensive branch rather than a shape
        // anything sends today.
        var subject = CompletionEmailCompositionService.GetSubject(HatFor(string.Empty));

        // assert
        subject.Should().Be("The gift exchange has finished");
    }

    [Fact]
    public void ComposeEmail_SaysTheOrganizerCalledTheExchangeOver()
    {
        // act
        var body = _sut.ComposeEmail(HatFor("Family Christmas"), "Alice");

        // assert
        body.Should().Contain("Dear Alice,");
        body.Should().Contain("Ben (ben@example.com) has let us know that the gift exchange, Family Christmas, is over.");
        body.Should().Contain("We hope everybody came away with something they liked.");
    }

    [Fact]
    public void ComposeEmail_CarriesTheWholeDrawForTheRecord()
    {
        // act
        var body = _sut.ComposeEmail(HatFor("Family Christmas"), "Alice");

        // assert: every pairing, not only the reader's own. Nothing is secret once the organizer
        // has revealed the picks, and the list is the record participants are being sent.
        body.Should().Contain("Here's who picked whose name, for the record:");
        ReadAsSent(body).Should().Contain("Alice &rarr; 😀 Bob");
        ReadAsSent(body).Should().Contain("Bob &rarr; 😀 Charlie");
        ReadAsSent(body).Should().Contain("Charlie &rarr; 😀 Alice");
    }

    [Fact]
    public void ComposeEmail_NamesNobodyWhoWasNotDrawn()
    {
        // arrange: a participant with no pick recorded cannot happen from the close path, which
        // only accepts a hat that has been shaken. Half a row is worse than no row.
        var hat = HatFor("Family Christmas") with
        {
            Participants = [ParticipantFor("Alice", "alice@example.com", string.Empty)]
        };

        // act
        var body = _sut.ComposeEmail(hat, "Alice");

        // assert
        body.Should().NotContain("for the record");
        body.Should().NotContain("&rarr;");

        // The rest of the email still goes: they are owed the news that it is over either way.
        body.Should().Contain("is over.");
    }

    [Fact]
    public void ComposeEmail_EncodesEveryValueItPlacesIntoTheHtml()
    {
        // arrange: the validators reject angle brackets in names, but this body is assembled by
        // string concatenation and should not depend on that holding.
        var hat = HatFor("Ampersand & Co") with
        {
            Organizer = new Person { Name = "<script>alert(1)</script>", Email = "evil\"@example.com" },
            Participants = [ParticipantFor("<script>a</script>", "a@example.com", "<img src=x onerror=alert(1)>")]
        };

        // act
        var body = _sut.ComposeEmail(hat, "<script>b</script>");

        // assert
        body.Should().NotContain("<script>");
        body.Should().Contain("&lt;script&gt;");
        body.Should().Contain("&lt;img src=x onerror=alert(1)&gt;");

        // Stated as a count rather than as an absence: the masthead is a real image, so an
        // injected tag would open a second one.
        body.Split("<img").Length.Should().Be(
            2,
            "the branding masthead is the only <img> this email is allowed to carry");
    }

    [Fact]
    public void ComposeEmail_CarriesTheBrandingAndSaysWhereItCameFrom()
    {
        // act
        var body = _sut.ComposeEmail(HatFor("Family Christmas"), "Alice");

        // assert
        body.Should().StartWith("<a href=\"https://namesoutofahat.com\"><img");
        body.Should().Contain("This email was sent on behalf of ben@example.com");

        // Nothing reads a reply to the sending address, so the way to reach a human is the
        // organizer, and the email has to say so.
        body.Should().Contain("mailto:ben@example.com");
        body.Should().Contain("Nobody reads replies to this address");
    }

    [Fact]
    public void ComposeEmail_KeepsTheGiftIdeasMachineryOutOfIt()
    {
        // act
        var body = _sut.ComposeEmail(HatFor("Family Christmas"), "Alice");

        // assert: the exchange is over, so a link to share ideas would only lead to a page saying
        // it isn't available. Nothing here should offer it.
        body.Should().NotContain("SHARE GIFT IDEAS");
        body.Should().NotContain("/ideas/");
    }

    private static Hat HatFor(string hatName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Name = hatName,
            Status = HatStatus.CooledOff,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            Organizer = new Person { Name = "Ben", Email = "ben@example.com" },
            Participants =
            [
                ParticipantFor("Alice", "alice@example.com", "Bob"),
                ParticipantFor("Bob", "bob@example.com", "Charlie"),
                ParticipantFor("Charlie", "charlie@example.com", "Alice")
            ],
            InvitationsQueuedDate = DateTimeOffset.MinValue
        };

    /// <summary>
    /// The same face the invitation used, because both now read it off the participant rather than
    /// deriving it from the name. An organizer who changed somebody's face after invitations went
    /// out sees the new one here, which is the cost of storing it and is the right way round: this
    /// message is written now, and the invitation was written then.
    /// </summary>
    [Fact]
    public void ComposeEmail_MarksBothNamesInARowWithThatPersonsOwnFace()
    {
        // arrange: everybody wears something different, so a line carrying the wrong face is a
        // failure rather than a coincidence.
        var hat = HatFor("Family Christmas") with
        {
            Participants =
            [
                ParticipantFor("Alice", "alice@example.com", "Bob", "😀"),
                ParticipantFor("Bob", "bob@example.com", "Charlie", "🤠"),
                ParticipantFor("Charlie", "charlie@example.com", "Alice", "🥳")
            ]
        };

        // act
        var body = _sut.ComposeEmail(hat, "Alice");

        // assert: each name carries its own face, on whichever side of the arrow it falls.
        ReadAsSent(body).Should().Contain("😀 Alice &rarr; 🤠 Bob");
        ReadAsSent(body).Should().Contain("🤠 Bob &rarr; 🥳 Charlie");
        ReadAsSent(body).Should().Contain("🥳 Charlie &rarr; 😀 Alice");
    }

    /// <summary>
    /// Two people in one exchange may share a name, and a list in which Sam gives to Sam says
    /// nothing. Both are named with their address; everybody else keeps their bare name.
    /// </summary>
    [Fact]
    public void ComposeEmail_GivenTwoParticipantsSharingAName_NamesBothWithTheirAddress()
    {
        // arrange
        var alice = new Person { Name = "Alice", Email = "alice@example.com" };
        var firstSam = new Person { Name = "Sam", Email = "sam.one@example.com" };
        var secondSam = new Person { Name = "Sam", Email = "sam.two@example.com" };

        var hat = HatFor("Family Christmas") with
        {
            Participants =
            [
                Participants.Empty with { Person = alice, PickedRecipient = firstSam, Emoji = "😀" },
                Participants.Empty with { Person = firstSam, PickedRecipient = secondSam, Emoji = "🤠" },
                Participants.Empty with { Person = secondSam, PickedRecipient = alice, Emoji = "🥳" }
            ]
        };

        // act
        var body = ReadAsSent(_sut.ComposeEmail(hat, "Alice"));

        // assert
        body.Should().Contain("😀 Alice &rarr; 🤠 Sam (sam.one@example.com)");
        body.Should().Contain("🤠 Sam (sam.one@example.com) &rarr; 🥳 Sam (sam.two@example.com)");
        body.Should().Contain("🥳 Sam (sam.two@example.com) &rarr; 😀 Alice");
        body.Should().NotContain("alice@example.com");
    }

    /// <summary>
    /// Names of very different lengths, which is the case the table is for: read as running text
    /// the arrows would start in three different places.
    /// </summary>
    [Fact]
    public void ComposeEmail_GivesEveryNameAndFaceACellOfItsOwnSoTheRowsLineUp()
    {
        // act
        var body = _sut.ComposeEmail(HatFor("Family Christmas"), "Alice");

        // assert: one row per pairing, and five cells in each — a face and a name on both sides of
        // the arrow, with the arrow between them.
        body.Should().Contain("<table");
        Regex.Matches(body, "<tr>").Count.Should().Be(3);
        Regex.Matches(body, "<td").Count.Should().Be(15);
    }

    /// <summary>
    /// The email as a reader sees it: tags replaced by a space and whitespace collapsed, so an
    /// assertion about what a row says is not also an assertion about the padding lining it up.
    /// </summary>
    private static string ReadAsSent(string body) =>
        Regex.Replace(Regex.Replace(body, "<[^>]+>", " "), @"\s+", " ").Trim();

    private static Participant ParticipantFor(
        string name,
        string email,
        string pickedRecipient,
        string emoji = "😀"
    ) =>
        new()
        {
            Person = new Person { Name = name, Email = email },
            // Everybody in these hats is at their own name at example.com, so a pick named here is
            // found at the address it would have in the hat.
            PickedRecipient = string.IsNullOrEmpty(pickedRecipient)
                ? Persons.Empty
                : new Person { Name = pickedRecipient, Email = $"{pickedRecipient.ToLowerInvariant()}@example.com" },
            EligibleRecipients = [],
            Emoji = emoji,
            DeliveryStatus = DeliveryStatus.Unknown,
            DeliveryDetail = string.Empty,
            DeliveryMessageType = string.Empty,
            DeliveryOccurredAt = DateTimeOffset.MinValue
        };
}
