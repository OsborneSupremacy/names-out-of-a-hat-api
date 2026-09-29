using GiftExchange.Library.Contexts;
using GiftExchange.Library.Utility;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The three do-not-add lists, against a real database.
///
/// What is worth pinning down is the scope of each list and the fact that all three answer at once.
/// A list that is too narrow silently stops blocking; a list that is too wide takes somebody out of
/// an exchange they never refused. The two failures look identical from the call site — a boolean —
/// so the tests below are mostly about which exchange and which organizer the answer applies to.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DoNotAddServiceTests
{
    static DoNotAddServiceTests() => DotEnv.Load();

    private readonly GiftExchangeProvider _provider;

    private readonly DoNotAddService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    /// <summary>
    /// An address nothing else in this class has used.
    /// </summary>
    /// <remarks>
    /// One Postgres container serves the whole collection, and nothing ever removes a row from
    /// these lists — which is the product behaviour, not a gap in the fixture. A fixed address
    /// would therefore arrive at the next test already refused, and the test that noticed would be
    /// the one asserting somebody was <em>not</em> refused.
    /// </remarks>
    private static string AnAddress() => $"person-{Guid.CreateVersion7():N}@example.com";

    public DoNotAddServiceTests(PostgresFixture dbFixture)
    {
        IDbContextFactory<GiftExchangeDbContext> contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _sut = serviceProvider.GetRequiredService<DoNotAddService>();
    }

    [Fact]
    public async Task AnUnknownAddress_IsNotRefused()
    {
        // arrange
        var hat = await SeedHatAsync();

        // act
        var refused = await _sut.IsRefusedAsync(AnAddress(), hat.OrganizerEmail, hat.HatId);

        // assert
        refused.Should().BeFalse();
    }

    [Fact]
    public async Task RefusingOneExchange_LeavesEveryOtherExchangeOpen()
    {
        // arrange
        var left = await SeedHatAsync();
        var another = await SeedHatAsync(left.OrganizerEmail);

        var alice = AnAddress();
        await RecordAsync(left, alice);

        // act
        var refusedByTheOneTheyLeft = await _sut.IsRefusedAsync(alice, left.OrganizerEmail, left.HatId);
        var refusedByAnother = await _sut.IsRefusedAsync(alice, another.OrganizerEmail, another.HatId);

        // assert: this is the narrowest list, and the one written without being asked for. Leaving
        // one exchange must not quietly opt somebody out of the same organizer's next one — that is
        // a separate choice, on a separate list.
        refusedByTheOneTheyLeft.Should().BeTrue();
        refusedByAnother.Should().BeFalse("leaving one exchange is not a statement about any other");
    }

    [Fact]
    public async Task RefusingAnOrganizer_CoversEveryExchangeOfTheirsAndNobodyElses()
    {
        // arrange
        var left = await SeedHatAsync();
        var sameOrganizerNextYear = await SeedHatAsync(left.OrganizerEmail);
        var somebodyElses = await SeedHatAsync();

        var alice = AnAddress();
        await RecordAsync(left, alice, blockOrganizer: true);

        // act
        var refusedNextYear = await _sut
            .IsRefusedAsync(alice, sameOrganizerNextYear.OrganizerEmail, sameOrganizerNextYear.HatId);

        var refusedElsewhere = await _sut
            .IsRefusedAsync(alice, somebodyElses.OrganizerEmail, somebodyElses.HatId);

        // assert
        refusedNextYear.Should().BeTrue("this is the list for an organizer who adds them every year");
        refusedElsewhere.Should().BeFalse("refusing one organizer says nothing about anybody else");
    }

    [Fact]
    public async Task RefusingEverything_CoversAnOrganizerTheyHaveNeverMet()
    {
        // arrange
        var left = await SeedHatAsync();
        var strangers = await SeedHatAsync();

        var alice = AnAddress();
        await RecordAsync(left, alice, blockAnywhere: true);

        // act
        var refused = await _sut.IsRefusedAsync(alice, strangers.OrganizerEmail, strangers.HatId);

        // assert
        refused.Should().BeTrue();
    }

    [Theory]
    // Every one of these delivers to the inbox that refused, so re-adding under any of them would
    // put the invitation in front of the person who asked never to get one.
    [InlineData("alice.smith+work@gmail.com", "alice.smith+hat@gmail.com")]
    [InlineData("alice.smith@gmail.com", "alicesmith@googlemail.com")]
    [InlineData("alice+work@example.com", "alice@example.com")]
    public async Task RefusingEverything_CoversEverySpellingOfTheSameInbox(string refusedAs, string addedAs)
    {
        // arrange: unique local parts, since the lists are shared by the whole suite.
        var unique = Guid.CreateVersion7().ToString("N");
        refusedAs = refusedAs.Replace("alice", $"alice{unique}");
        addedAs = addedAs.Replace("alice", $"alice{unique}");

        var left = await SeedHatAsync();
        var strangers = await SeedHatAsync();
        await RecordAsync(left, refusedAs, blockAnywhere: true);

        // act
        var refused = await _sut.IsRefusedAsync(addedAs, strangers.OrganizerEmail, strangers.HatId);

        // assert
        refused.Should().BeTrue();
    }

    [Theory]
    // The address is stored lower-cased and trimmed, so none of these is a different person from
    // the one who refused. An organizer retyping an address from memory produces exactly these.
    [InlineData("upper")]
    [InlineData("mixed")]
    [InlineData("padded")]
    public async Task MatchingIgnoresCaseAndSurroundingSpace(string mangling)
    {
        // arrange
        var hat = await SeedHatAsync();
        var alice = AnAddress();
        await RecordAsync(hat, alice);

        var typedByTheOrganizer = mangling switch
        {
            "upper" => alice.ToUpperInvariant(),
            "mixed" => char.ToUpperInvariant(alice[0]) + alice[1..],
            _ => $"  {alice}  "
        };

        // act
        var refused = await _sut.IsRefusedAsync(typedByTheOrganizer, hat.OrganizerEmail, hat.HatId);

        // assert
        refused.Should().BeTrue();
    }

    [Fact]
    public async Task TheOrganizerListIsMatchedCaseInsensitivelyOnBothAddresses()
    {
        // arrange: the organizer's own address is the scope of that list, so it has to normalize
        // the same way the refusing address does. Missing this one would leave the block in place
        // and silently stop it matching.
        var organizer = AnAddress();
        var alice = AnAddress();

        var hat = await SeedHatAsync(char.ToUpperInvariant(organizer[0]) + organizer[1..]);
        await RecordAsync(hat, alice, blockOrganizer: true);

        var another = await SeedHatAsync(organizer.ToUpperInvariant());

        // act
        var refused = await _sut.IsRefusedAsync(alice.ToUpperInvariant(), another.OrganizerEmail, another.HatId);

        // assert
        refused.Should().BeTrue();
    }

    [Fact]
    public async Task RecordingTheSameRefusalConcurrently_IsNotAnError()
    {
        // arrange: two tabs submitting at once. The read that guards each insert sees nothing in
        // both transactions, so the unique indexes are what stop the second becoming an error the
        // person leaving has to look at.
        var hat = await SeedHatAsync();
        var alice = AnAddress();

        // act
        var both = async () => await Task.WhenAll(
            RecordAsync(hat, alice, blockOrganizer: true, blockAnywhere: true),
            RecordAsync(hat, alice, blockOrganizer: true, blockAnywhere: true));

        // assert
        await both.Should().NotThrowAsync();
        (await _sut.IsRefusedAsync(alice, hat.OrganizerEmail, hat.HatId)).Should().BeTrue();
    }

    [Fact]
    public async Task RecordingTheSameRefusalTwice_IsNotAnError()
    {
        // arrange: two tabs, a double submit, or an organizer removing somebody who was already
        // leaving. The unique indexes are what make the second one a no-op.
        var hat = await SeedHatAsync();

        var alice = AnAddress();

        // act
        await RecordAsync(hat, alice, blockOrganizer: true, blockAnywhere: true);
        var second = async () => await RecordAsync(hat, alice, blockOrganizer: true, blockAnywhere: true);

        // assert
        await second.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ManyAddressesAtOnce_ComeBackAsJustTheRefusedOnes()
    {
        // arrange: the shape copying an exchange uses. Three lists, one round trip, many addresses.
        var hat = await SeedHatAsync();

        var leftThisOne = AnAddress();
        var leftTheOrganizer = AnAddress();
        var leftEverything = AnAddress();
        var stillHappy = AnAddress();

        await RecordAsync(hat, leftThisOne);
        await RecordAsync(hat, leftTheOrganizer, blockOrganizer: true);
        await RecordAsync(hat, leftEverything, blockAnywhere: true);

        // act
        var refused = await _sut.FindRefusedAsync(new DoNotAddCheckRequest
        {
            Emails =
            [
                leftThisOne.ToUpperInvariant(),
                leftTheOrganizer,
                leftEverything.ToUpperInvariant(),
                stillHappy
            ],
            OrganizerEmail = hat.OrganizerEmail,
            HatId = hat.HatId
        });

        // assert: normalized, because that is the only form in which two spellings are one address.
        refused.Should().BeEquivalentTo([leftThisOne, leftTheOrganizer, leftEverything]);
    }

    [Fact]
    public async Task AnAddressOnMoreThanOneList_IsReportedOnce()
    {
        // arrange
        var hat = await SeedHatAsync();
        var alice = AnAddress();
        await RecordAsync(hat, alice, blockOrganizer: true, blockAnywhere: true);

        // act
        var refused = await _sut.FindRefusedAsync(new DoNotAddCheckRequest
        {
            Emails = [alice],
            OrganizerEmail = hat.OrganizerEmail,
            HatId = hat.HatId
        });

        // assert: a set, not a list. The count is read by CopyHatService to tell an organizer how
        // many people were left out, and counting somebody three times would overstate it.
        refused.Should().ContainSingle();
    }

    private Task RecordAsync(
        HatDataModel hat,
        string email,
        bool blockOrganizer = false,
        bool blockAnywhere = false
    ) =>
        _provider.RecordDoNotAddAsync(new RecordDoNotAddRequest
        {
            Email = email,
            HatId = hat.HatId,
            OrganizerEmail = hat.OrganizerEmail,
            BlockOrganizer = blockOrganizer,
            BlockAnywhere = blockAnywhere
        });

    private async Task<HatDataModel> SeedHatAsync(string organizerEmail = "")
    {
        var hat = _hatFaker.Generate();

        if (!string.IsNullOrWhiteSpace(organizerEmail))
            hat = hat with { OrganizerEmail = organizerEmail };

        await _provider.CreateHatAsync(hat);

        return hat;
    }
}
