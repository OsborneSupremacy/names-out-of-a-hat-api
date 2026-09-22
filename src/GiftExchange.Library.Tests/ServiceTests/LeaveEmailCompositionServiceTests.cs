namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The two messages that go out when somebody leaves.
///
/// These are a pair, and the whole design of the first is a consequence of what the second is
/// allowed to say. Read together they are the only place the leaver's name is deliberately in one
/// email and deliberately not in the other, so they are tested together.
/// </summary>
public class LeaveEmailCompositionServiceTests
{
    private readonly LeaveEmailCompositionService _sut = new();

    private static readonly Person Leaver = new() { Name = "Alice", Email = "alice@example.com" };

    [Fact]
    public void ParticipantNotice_NamesNobody()
    {
        // act
        var body = _sut.ComposeParticipantNotice(HatFor("Family Christmas"));

        // assert: the one secret this feature keeps. Nothing here identifies who left, and nothing
        // here says how many are left either — that is the same fact by arithmetic.
        body.Should().NotContain(Leaver.Name);
        body.Should().NotContain(Leaver.Email);
        body.Should().Contain("Somebody has asked to leave");
    }

    [Fact]
    public void ParticipantNotice_TellsThemTheirNameIsVoidAndWhoHasToFixIt()
    {
        // act
        var body = _sut.ComposeParticipantNotice(HatFor("Family Christmas"));

        // assert: three things, in the order somebody needs them. The last is there so nobody sits
        // waiting on this application to fix something only a person can.
        body.Should().Contain("disregard the name you were assigned");
        body.Should().Contain("Ben will need to shake the hat again");
        body.Should().Contain("Family Christmas");
    }

    [Fact]
    public void ParticipantNotice_AsksThemNotToWorkItOut()
    {
        // act
        var body = _sut.ComposeParticipantNotice(HatFor("Family Christmas"));

        // assert: not enforceable, and worth saying anyway. In a small exchange the arithmetic is
        // easy, and the only thing standing between the leaver and being identified is that nobody
        // bothers.
        body.Should().Contain("please don't ask around");
    }

    [Fact]
    public void ParticipantSubject_DoesNotAnnounceTheNewsInAPreviewPane()
    {
        // act
        var subject = LeaveEmailCompositionService.GetParticipantSubject(HatFor("Family Christmas"));

        // assert: a subject sits unread in an inbox next to a sender, where anybody looking over a
        // shoulder can read it. The body says it plainly enough.
        subject.Should().NotContain("left");
        subject.Should().NotContain("leave");
        subject.Should().Contain("Family Christmas");
    }

    [Fact]
    public void OrganizerNotice_NamesThemAndGivesTheAdvice()
    {
        // act
        var body = _sut.ComposeOrganizerNotice(HatFor("Family Christmas"), Leaver, namesMustBeDrawnAgain: true);

        // assert
        body.Should().Contain("Alice");
        body.Should().Contain("alice@example.com");
        body.Should().Contain("can't be added to this gift exchange again");
        body.Should().Contain("shake the hat again");

        // The advice is as much the point of this email as the news is: nearly every leave traces
        // back to somebody being entered into an exchange without being asked.
        body.Should().Contain("check with somebody before you add them");
    }

    [Fact]
    public void OrganizerNotice_ForAFinishedExchange_DoesNotSendThemLookingForARedrawButton()
    {
        // act
        var body = _sut.ComposeOrganizerNotice(HatFor("Family Christmas"), Leaver, namesMustBeDrawnAgain: false);

        // assert
        body.Should().NotContain("shake the hat again");
        body.Should().Contain("had already finished");
        body.Should().Contain("check with somebody before you add them",
            "the advice applies whatever state the exchange was in");
    }

    [Fact]
    public void BothNotices_EncodeWhateverTheOrganizerTyped()
    {
        // arrange
        var hat = HatFor("<script>alert(1)</script>") with
        {
            Organizer = new Person { Name = "<b>Ben</b>", Email = "ben@example.com" }
        };

        var attacker = new Person { Name = "<img src=x onerror=y>", Email = "alice@example.com" };

        // act
        var participantNotice = _sut.ComposeParticipantNotice(hat);
        var organizerNotice = _sut.ComposeOrganizerNotice(hat, attacker, namesMustBeDrawnAgain: true);

        // assert
        participantNotice.Should().NotContain("<script>");
        participantNotice.Should().NotContain("<b>Ben</b>");
        organizerNotice.Should().NotContain("<script>");
        organizerNotice.Should().NotContain("<img src=x");
    }

    private static Hat HatFor(string hatName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Name = hatName,
            Status = HatStatus.InvitationsSent,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            Organizer = new Person { Name = "Ben", Email = "ben@example.com" },
            Participants = [],
            InvitationsQueuedDate = DateTimeOffset.MinValue,
            ExchangeDate = DateOnly.MinValue
        };
}
