namespace GiftExchange.Library.Tests.ServiceTests;

public class EmailCompositionServiceTests
{
    private readonly EmailCompositionService _sut = new();

    [Fact]
    public void ComposeEmail_NamesTheOrganizersAddressAlongsideTheirName()
    {
        // act
        var body = _sut.ComposeEmail(Invitation());

        // assert
        body.Should().Contain("Ben (ben@example.com) has added you to the gift exchange, Family Christmas!");
    }

    [Theory]
    // A name that does read as a noun phrase, which is the easy case.
    [InlineData("Family Christmas", "Ben has added you to the gift exchange, Family Christmas!")]
    // And one that does not. "added you to the Christmas On August 27!" was the version of this
    // line that made the phrasing worth changing.
    [InlineData("Christmas On August 27", "Ben has added you to the gift exchange, Christmas On August 27!")]
    // A name carrying its own article no longer needs handling of its own: nothing is prepended to
    // the name at all, so there is no second article to avoid.
    [InlineData("The Osborne Exchange", "Ben has added you to the gift exchange, The Osborne Exchange!")]
    public void GetSubject_NamesTheExchangeWithoutBuildingTheSentenceAroundItsName(
        string hatName,
        string expected)
    {
        // act
        var subject = EmailCompositionService.GetSubject(HatFor("Ben", "ben@example.com", hatName));

        // assert
        subject.Should().Be(expected);
    }

    [Fact]
    public void GetSubject_GivenNoName_StillReads()
    {
        // act
        var subject = EmailCompositionService.GetSubject(HatFor("Ben", "ben@example.com", string.Empty));

        // assert: the validators require a name, so this is the defensive branch rather than a
        // shape anything sends today.
        subject.Should().Be("Ben has added you to the gift exchange!");
    }

    [Fact]
    public void ComposeEmail_CarriesTheSmallPrintAndTheOrganizersAddress()
    {
        // act
        var body = _sut.ComposeEmail(Invitation());

        // assert
        body.Should().Contain("This email was sent on behalf of ben@example.com");
        body.Should().Contain("namesoutofahat.com is not responsible for them");
        body.Should().Contain("namesoutofahat.com");
        body.Should().NotContain("namesoutofhat.com", "the domain is namesoutofahat.com");
    }

    [Fact]
    public void ComposeEmail_EncodesEveryValueItPlacesIntoTheHtml()
    {
        // arrange: the validators reject angle brackets in names, but this body is assembled by
        // string concatenation and should not depend on that holding.
        var hat = HatFor("<script>alert(1)</script>", "evil\"@example.com", "Ampersand & Co")
            with { PriceRange = "<b>$20</b>", AdditionalInformation = "<img src=x onerror=alert(1)>" };

        // act
        var body = _sut.ComposeEmail(Invitation(hat, participantName: "<script>a</script>", pickedName: "<script>b</script>"));

        // assert
        // What makes the payloads inert is the escaped angle brackets, not the absence of the
        // words: "onerror=" survives as harmless text once "<img" cannot open a tag.
        body.Should().NotContain("<script>");

        // This used to read NotContain("<img"), which worked only while invitations carried no
        // images at all. The masthead is now a real one, so the same guarantee has to be stated as
        // a count: an injected tag would open a second.
        body.Split("<img").Length.Should().Be(
            2,
            "the branding masthead is the only <img> an invitation is allowed to carry");

        body.Should().Contain("&lt;script&gt;");
        body.Should().Contain("&lt;img src=x onerror=alert(1)&gt;");
        body.Should().Contain("Ampersand &amp; Co");
    }

    /// <summary>
    /// The face beside the picked name is the one that person wears in this hat, handed to the
    /// composer rather than worked out from the name. That is what lets an organizer change it and
    /// what keeps this message agreeing with the announcement sent at the end of the exchange.
    /// </summary>
    [Fact]
    public void ComposeEmail_MarksThePickedNameWithTheFaceItWasGiven()
    {
        // act
        var body = _sut.ComposeEmail(Invitation(pickedName: "Charlie", pickedEmoji: "🥳"));

        // assert
        body.Should().Contain("<b>🥳 Charlie</b>");
    }

    [Fact]
    public void ComposeEmail_GivenNoFace_StillNamesThePick()
    {
        // act: nothing sends this today -- every participant is given a face as they are added --
        // so it is the defensive branch, and what matters is that the name survives it.
        var body = _sut.ComposeEmail(Invitation(pickedName: "Charlie", pickedEmoji: string.Empty));

        // assert
        body.Should().Contain("<b> Charlie</b>");
    }

    [Fact]
    public void ComposeEmail_OpensWithTheBrandingMasthead()
    {
        // act
        var body = _sut.ComposeEmail(Invitation());

        // assert
        body.Should().StartWith("<a href=\"https://namesoutofahat.com\"><img");
        body.Should().Contain("src=\"https://namesoutofahat.com/logo-horizontal.png\"");

        // Most clients block remote images until asked, so the alt text is what the majority of
        // recipients actually read. It has to say the product name on its own.
        body.Should().Contain("alt=\"Names Out Of A Hat\"");

        // Outlook lays out from the attributes rather than the CSS, so dropping them collapses the
        // space the masthead should occupy.
        body.Should().Contain("width=\"260\"");
        body.Should().Contain("height=\"87\"");
    }

    [Fact]
    public void ComposeEmail_CarriesAGiftIdeasButtonAddressedToTheParticipantsOwnToken()
    {
        // act
        var body = _sut.ComposeEmail(Invitation(giftIdeasToken: "abc123"));

        // assert
        body.Should().Contain("SHARE GIFT IDEAS");
        body.Should().Contain("mailto:abc123@ideas.namesoutofahat.com");

        // Printed in full as well as linked. A client that is not registered as the handler for
        // mailto: does nothing at all when the button is clicked, with no error to explain it.
        body.Should().Contain("abc123@ideas.namesoutofahat.com");
    }

    [Fact]
    public void ComposeEmail_GivesTheGiftIdeasButtonAMailtoRatherThanAReply()
    {
        // act
        var body = _sut.ComposeEmail(Invitation(giftIdeasToken: "abc123"));

        // assert: this is a security property, not a styling one. This email names the recipient's
        // own pick, so a reply would quote it, and the quoted text would be forwarded to the one
        // person who must never learn it. A mailto: opens an empty message with nothing to quote.
        body.Should().NotContain("href=\"mailto:donotreply");
        body.Should().Contain("Please do not reply to this email",
            "the warning is what steers somebody away from the reply button and towards the button");
    }

    [Fact]
    public void ComposeEmail_GivenNoToken_LeavesTheGiftIdeasBlockOutEntirely()
    {
        // act: an invitation with no token issued gets no block, rather than a button addressed to
        // "@ideas.namesoutofahat.com" that silently routes nowhere.
        var body = _sut.ComposeEmail(Invitation(giftIdeasToken: string.Empty));

        // assert
        body.Should().NotContain("SHARE GIFT IDEAS");
        body.Should().NotContain("ideas.namesoutofahat.com");
    }

    [Theory]
    // The phrasing itself is covered by PriceRangePhrasingTests; this is about the body actually
    // carrying it, and about the line disappearing entirely when no price was set.
    [InlineData("around $100", "Please purchase a gift costing around $100.")]
    [InlineData("$25 - $40", "Please purchase a gift costing $25 - $40.")]
    [InlineData("Keep it under $20", "Keep it under $20.")]
    public void ComposeEmail_GivenAPrice_CarriesItAsASentenceThatReads(string priceRange, string expected)
    {
        // arrange
        var hat = HatFor("Ben", "ben@example.com", "Family Christmas") with { PriceRange = priceRange };

        // act
        var body = _sut.ComposeEmail(Invitation(hat));

        // assert
        body.Should().Contain(expected);
    }

    [Fact]
    public void ComposeEmail_GivenNoPrice_LeavesTheLineOut()
    {
        // act
        var body = _sut.ComposeEmail(Invitation());

        // assert
        body.Should().NotContain("Please purchase a gift");
    }

    [Fact]
    public void ComposeEmail_GivenALeaveToken_PutsTheWayOutInTheFinePrint()
    {
        // act
        var body = _sut.ComposeEmail(Invitation(leaveToken: "leave-abc123"));

        // assert: present, and in the small print rather than above it. Somebody who wants out will
        // look for it; putting it higher would make an ordinary invitation read as though leaving
        // were the expected response to one.
        body.Should().Contain("https://api.namesoutofahat.com/leave/leave-abc123");
        body.Should().Contain("leave this gift exchange");

        var finePrint = body[body.IndexOf("<small", StringComparison.Ordinal)..];
        finePrint.Should().Contain("/leave/leave-abc123", "the link belongs inside the small print");
    }

    [Fact]
    public void ComposeEmail_GivenNoLeaveToken_LeavesTheSentenceOutEntirely()
    {
        // act: the organizer's own copy. No leave token is ever issued for them, so the sentence is
        // not written rather than written and pointed nowhere.
        var body = _sut.ComposeEmail(Invitation(participantName: "Ben", leaveToken: string.Empty));

        // assert
        body.Should().NotContain("leave this gift exchange");
        body.Should().NotContain("/leave/");
    }

    [Fact]
    public void ComposeEmail_TheLeaveSentenceDoesNotPromiseAnonymityItCannotKeep()
    {
        // act
        var body = _sut.ComposeEmail(Invitation(leaveToken: "leave-abc123"));

        // assert: the organizer <em>is</em> told who left — they cannot run the exchange otherwise.
        // What is kept from everybody else is said here so that nobody leaves on a false premise.
        body.Should().Contain("without saying who");
    }

    /// <summary>
    /// The ordinary invitation, with only what a test is about spelled out.
    /// </summary>
    /// <remarks>
    /// Every argument is named at the call site, which is the point of the record this builds:
    /// four of the five fields are strings, and the arguments most worth not transposing — the
    /// participant's name against the name they drew, the gift ideas token against the leave one —
    /// are exactly the ones that used to sit next to each other positionally.
    /// </remarks>
    private static ComposeInvitationRequest Invitation(
        Hat? hat = null,
        string participantName = "Alice",
        string pickedName = "Charlie",
        string pickedEmoji = "🤠",
        string giftIdeasToken = "token",
        string leaveToken = "leave-token"
    ) =>
        new()
        {
            Hat = hat ?? HatFor("Ben", "ben@example.com", "Family Christmas"),
            ParticipantName = participantName,
            PickedName = pickedName,
            PickedEmoji = pickedEmoji,
            GiftIdeasToken = giftIdeasToken,
            LeaveToken = leaveToken
        };

    private static Hat HatFor(string organizerName, string organizerEmail, string hatName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Name = hatName,
            Status = HatStatus.NamesAssigned,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            Organizer = new Person { Name = organizerName, Email = organizerEmail },
            Participants = [],
            InvitationsQueuedDate = DateTimeOffset.MinValue
        };
}
