namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The plain-text part is derived from the HTML, so what these hold it to is that nothing the HTML
/// says is lost on the way: every sentence, and every link's address alongside its words.
/// </summary>
public class EmailPlainTextTests
{
    [Fact]
    public void FromHtml_GivenAnInvitation_KeepsThePickAndEveryLinkAndNoMarkup()
    {
        // arrange
        var html = new EmailCompositionService().ComposeEmail(new ComposeInvitationRequest
        {
            Hat = new Hat
            {
                Id = Guid.CreateVersion7(),
                Name = "Family Christmas",
                Status = HatStatus.NamesAssigned,
                AdditionalInformation = "Bring it wrapped & labelled",
                PriceRange = string.Empty,
                Organizer = new Person { Name = "Ben", Email = "ben@example.com" },
                Participants = [],
                InvitationsQueuedDate = DateTimeOffset.MinValue,
                ExchangeDate = DateOnly.MinValue
            },
            ParticipantName = "Alice",
            PickedName = "Charlie",
            PickedEmoji = "🤠",
            GiftIdeasToken = "ideas-token",
            LeaveToken = "leave-token"
        });

        // act
        var text = EmailPlainText.FromHtml(html);

        // assert
        text.Should().Contain("Dear Alice,");
        text.Should().Contain("🤠 Charlie");
        text.Should().Contain("Bring it wrapped & labelled", "entities are decoded");
        text.Should().Contain($"({Branding.ApiUrl}/ask/ideas-token)", "a button is useless without its address");
        text.Should().Contain($"leave this gift exchange ({EmailCompositionService.LeaveUrlFor("leave-token")})");
        text.Should().Contain("contact Ben (ben@example.com)", "a mailto link gives the address, not the scheme");
        text.Should().NotContain("<").And.NotContain("&mdash;").And.NotContain("style=");
        text.Should().NotContain("\n\n\n", "paragraphs are separated by one blank line, not a run of them");
    }

    [Fact]
    public void FromHtml_GivenATable_KeepsEachRowOnOneLine()
    {
        // arrange: the shape of the completion email's list of who drew whom.
        const string html =
            """
            <table>
              <tr><td>🤠</td><td>Alice</td><td>&rarr;</td><td>🎩</td><td><b>Bob</b></td></tr>
              <tr><td>🎩</td><td>Bob</td><td>&rarr;</td><td>🤠</td><td><b>Alice</b></td></tr>
            </table>
            """;

        // act
        var lines = EmailPlainText.FromHtml(html).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // assert
        lines.Should().Equal("🤠 Alice → 🎩 Bob", "🎩 Bob → 🤠 Alice");
    }

    [Fact]
    public void FromHtml_GivenAnImageLink_UsesTheAltText()
    {
        // act
        var text = EmailPlainText.FromHtml(EmailBranding.Masthead());

        // assert
        text.Trim().Should().Be($"{Branding.LogoAltText} ({Branding.SiteUrl})");
    }

    [Fact]
    public void FromHtml_GivenALinkWhoseWordsAreItsAddress_DoesNotRepeatIt()
    {
        // act
        var text = EmailPlainText.FromHtml("""<a href="https://namesoutofahat.com">https://namesoutofahat.com</a>""");

        // assert
        text.Trim().Should().Be("https://namesoutofahat.com");
    }

    [Fact]
    public void FromHtml_SkipsStylesAndTitles()
    {
        // act
        var text = EmailPlainText.FromHtml(
            "<html><head><title>Hidden</title><style>p { color: red; }</style></head><body><p>Shown</p></body></html>");

        // assert
        text.Trim().Should().Be("Shown");
    }
}
