using GiftExchange.Library.Utility;
using GiftExchange.Library.Contexts;

namespace GiftExchange.Library.Tests.HandlerTests;

/// <summary>
/// The gift ideas side of the provider, against a real Postgres.
///
/// The queries here lean on things only a database has an opinion about — inner joins that resolve
/// through the sentinel participant, a unique index that makes reissuing a token replace rather
/// than duplicate — so an in-memory double would have proved nothing.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GiftIdeaProviderTests
{
    private readonly GiftExchangeProvider _sut;

    private readonly HatDataModelFaker _hatDataModelFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    public GiftIdeaProviderTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _sut = serviceProvider.GetRequiredService<GiftExchangeProvider>();
    }

    [Fact]
    public async Task IssueGiftIdeaTokensAsync_GivesEveryParticipantATokenAndStoresOnlyTheHash()
    {
        // arrange
        var exchange = await SeedExchangeAsync();

        // act
        var tokens = await _sut.IssueGiftIdeaTokensAsync(exchange.HatId);

        // assert
        tokens.Keys.Should().BeEquivalentTo(exchange.Emails);

        await using var context = _contextFactory.CreateDbContext();

        var stored = await context.GiftIdeaTokens
            .Where(token => exchange.ParticipantIds.Contains(token.ParticipantId))
            .Select(token => token.TokenHash)
            .ToListAsync();

        // What is stored is the digest, never the token. Anyone reading this table can identify a
        // token they already hold and derive none that they do not.
        stored.Should().BeEquivalentTo(tokens.Values.Select(SecretToken.Hash));
        stored.Should().NotIntersectWith(tokens.Values);
    }

    [Fact]
    public async Task IssueGiftIdeaTokensAsync_GivesEveryParticipantADifferentToken()
    {
        // arrange
        var exchange = await SeedExchangeAsync();

        // act
        var tokens = await _sut.IssueGiftIdeaTokensAsync(exchange.HatId);

        // assert: a shared token would route two people's ideas to one row.
        tokens.Values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task IssueGiftIdeaTokensAsync_WhenRunAgain_ReplacesTheEarlierTokenRatherThanAddingASecond()
    {
        // arrange
        var exchange = await SeedExchangeAsync();
        var first = await _sut.IssueGiftIdeaTokensAsync(exchange.HatId);

        // act
        var second = await _sut.IssueGiftIdeaTokensAsync(exchange.HatId);

        // assert
        second.Values.Should().NotIntersectWith(first.Values);

        await using var context = _contextFactory.CreateDbContext();

        var rows = await context.GiftIdeaTokens
            .Where(token => exchange.ParticipantIds.Contains(token.ParticipantId))
            .ToListAsync();

        rows.Should().HaveCount(exchange.ParticipantIds.Count, "one live token each");

        // The superseded address must stop working. Left behind, it would still write to the same
        // participant after they had been handed a new one.
        var (foundOld, _) = await _sut.FindGiftIdeaRouteAsync(SecretToken.Hash(first[exchange.Alpha.Email]));
        foundOld.Should().BeFalse();
    }

    [Fact]
    public async Task FindGiftIdeaRouteAsync_ResolvesTheSenderTheirPickAndWhoeverDrewThem()
    {
        // arrange: a three-way cycle, so the person the sender drew and the person who drew the
        // sender are different people. With two participants they would be the same, and the test
        // could not tell the two apart.
        var exchange = await SeedExchangeAsync();
        var tokens = await _sut.IssueGiftIdeaTokensAsync(exchange.HatId);

        // act
        var (found, route) = await _sut.FindGiftIdeaRouteAsync(SecretToken.Hash(tokens[exchange.Alpha.Email]));

        // assert
        found.Should().BeTrue();
        route.HatId.Should().Be(exchange.HatId);
        route.HatStatus.Should().Be(HatStatus.InProgress);
        route.Sender.Email.Should().Be(exchange.Alpha.Email);
        route.Sender.Name.Should().Be(exchange.Alpha.Name);

        // Alpha drew Beta, so Beta's name is what must never appear in Alpha's submitted text.
        route.SenderPickedRecipient.Name.Should().Be(exchange.Beta.Name);

        // Gamma drew Alpha, so Gamma is the one person these ideas are for.
        route.Giver.Email.Should().Be(exchange.Gamma.Email);
        route.Giver.Name.Should().Be(exchange.Gamma.Name);

        // Their row too, because whether a held submission is owed to anybody is a fact about a pair
        // of participants rather than about an address.
        route.GiverParticipantId.Should().Be(exchange.Gamma.ParticipantId);
    }

    [Fact]
    public async Task FindGiftIdeaRouteAsync_GivenAParticipantNobodyHasDrawn_StillResolvesTheSender()
    {
        // arrange: tokens are issued once names are assigned, so this should not arise. It is here
        // because the giver is read with an outer join for exactly this case, and an inner one
        // would have thrown the whole match away instead of returning nobody.
        var hat = await CreateHatAsync();
        var alpha = await AddParticipantAsync(hat, "Alpha");

        var tokens = await _sut.IssueGiftIdeaTokensAsync(hat.HatId);

        // act
        var (found, route) = await _sut.FindGiftIdeaRouteAsync(SecretToken.Hash(tokens[alpha.Email]));

        // assert
        found.Should().BeTrue();
        route.Sender.Email.Should().Be(alpha.Email);
        route.Giver.Email.Should().BeEmpty("nobody has drawn them");
        route.GiverParticipantId.Should().Be(Guid.Empty, "and so there is nobody who can have asked");
        route.SenderPickedRecipient.Name.Should().BeEmpty("they have not drawn anybody either");
    }

    [Fact]
    public async Task FindGiftIdeaRouteAsync_GivenAnUnknownHash_FindsNothing()
    {
        // arrange
        await SeedExchangeAsync();

        // act
        var (found, _) = await _sut.FindGiftIdeaRouteAsync(SecretToken.Hash(SecretToken.Create()));

        // assert
        found.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindGiftIdeaRouteAsync_GivenAnEmptyHash_FindsNothing(string hash)
    {
        // arrange
        await SeedExchangeAsync();

        // act
        var (found, route) = await _sut.FindGiftIdeaRouteAsync(hash);

        // assert: an empty token must never resolve to anybody, least of all the sentinel.
        found.Should().BeFalse();
        route.ParticipantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task AddGiftIdeaAsync_AppendsRatherThanOverwriting()
    {
        // arrange
        var exchange = await SeedExchangeAsync();

        // act
        await ShareOutrightAsync(exchange.Alpha.ParticipantId, "A cast iron skillet");
        await ShareOutrightAsync(exchange.Alpha.ParticipantId, "Actually, a bread book");

        // assert
        await using var context = _contextFactory.CreateDbContext();

        var stored = await context.GiftIdeas
            .Where(giftIdea => giftIdea.ParticipantId == exchange.Alpha.ParticipantId)
            .OrderBy(giftIdea => giftIdea.CreatedAt)
            .ToListAsync();

        // The first submission survives the second, so an abuse report can still be answered
        // against what was actually sent.
        stored.Select(giftIdea => giftIdea.Ideas)
            .Should().Equal("A cast iron skillet", "Actually, a bread book");
    }

    [Fact]
    public async Task DeleteParticipantAsync_TakesTheirGiftIdeasAndTheirTokenWithThem()
    {
        // arrange
        var exchange = await SeedExchangeAsync();
        await _sut.IssueGiftIdeaTokensAsync(exchange.HatId);
        await ShareOutrightAsync(exchange.Alpha.ParticipantId, "A scarf");

        // act
        await _sut.DeleteParticipantAsync(exchange.OrganizerEmail, exchange.HatId, exchange.Alpha.Email);

        // assert
        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeas.AnyAsync(row => row.ParticipantId == exchange.Alpha.ParticipantId))
            .Should().BeFalse();

        // A token left behind would keep a live address pointed at somebody no longer in the hat.
        (await context.GiftIdeaTokens.AnyAsync(row => row.ParticipantId == exchange.Alpha.ParticipantId))
            .Should().BeFalse();

        (await context.GiftIdeaTokens.AnyAsync(row => row.ParticipantId == exchange.Beta.ParticipantId))
            .Should().BeTrue("only the removed participant should be affected");
    }

    [Fact]
    public async Task DeleteHatAsync_TakesEveryGiftIdeaAndTokenInItWithIt()
    {
        // arrange
        var exchange = await SeedExchangeAsync();
        await _sut.IssueGiftIdeaTokensAsync(exchange.HatId);

        foreach (var participantId in exchange.ParticipantIds)
            await ShareOutrightAsync(participantId, "Something");

        // act
        await _sut.DeleteHatAsync(new DeleteHatRequest
        {
            HatId = exchange.HatId,
            OrganizerEmail = exchange.OrganizerEmail
        });

        // assert
        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeas.AnyAsync(row => exchange.ParticipantIds.Contains(row.ParticipantId)))
            .Should().BeFalse();
        (await context.GiftIdeaTokens.AnyAsync(row => exchange.ParticipantIds.Contains(row.ParticipantId)))
            .Should().BeFalse();
    }

    private async Task<HatDataModel> CreateHatAsync()
    {
        var hat = _hatDataModelFaker.Generate();
        await _sut.CreateHatAsync(hat);
        return hat;
    }

    private async Task<SeededParticipant> AddParticipantAsync(HatDataModel hat, string name)
    {
        var request = _participantFaker.Generate() with
        {
            HatId = hat.HatId,
            OrganizerEmail = hat.OrganizerEmail,
            Name = name
        };

        await _sut.CreateParticipantAsync(request, []);

        await using var context = _contextFactory.CreateDbContext();

        var participantId = await context.Participants
            .Where(participant => participant.HatId == hat.HatId && participant.Person.Email == request.Email)
            .Select(participant => participant.ParticipantId)
            .SingleAsync();

        return new SeededParticipant(participantId, name, request.Email);
    }

    /// <summary>
    /// A hat with three participants drawing in a cycle: Alpha drew Beta, Beta drew Gamma, Gamma
    /// drew Alpha. Three rather than two so that "who they drew" and "who drew them" are never the
    /// same person, which is what makes the routing assertions mean anything.
    /// </summary>
    private async Task<SeededExchange> SeedExchangeAsync()
    {
        var hat = await CreateHatAsync();

        var alpha = await AddParticipantAsync(hat, "Alpha");
        var beta = await AddParticipantAsync(hat, "Beta");
        var gamma = await AddParticipantAsync(hat, "Gamma");

        await _sut.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, alpha.Email, beta.Name);
        await _sut.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, beta.Email, gamma.Name);
        await _sut.UpdateParticipantPickedRecipientAsync(hat.OrganizerEmail, hat.HatId, gamma.Email, alpha.Name);

        return new SeededExchange(hat.HatId, hat.OrganizerEmail, alpha, beta, gamma);
    }

    [Fact]
    public async Task FindGiftIdeaContributionRouteAsync_MakesTheAskerTheGiver()
    {
        // arrange: Alpha drew Beta and asks Gamma what Beta might like.
        var exchange = await SeedExchangeAsync();

        var askToken = await _sut.IssueGiftIdeaAskAsync(
            exchange.Alpha.ParticipantId, exchange.Gamma.ParticipantId, exchange.Beta.ParticipantId);

        // act
        var (found, route) = await _sut.FindGiftIdeaContributionRouteAsync(SecretToken.Hash(askToken));

        // assert: the asker is the giver by a shorter route -- they asked because they drew the
        // subject -- and the id is theirs, not the helper's.
        found.Should().BeTrue();
        route.Giver.Email.Should().Be(exchange.Alpha.Email);
        route.GiverParticipantId.Should().Be(exchange.Alpha.ParticipantId);
        route.ParticipantId.Should().Be(exchange.Gamma.ParticipantId);
    }

    [Fact]
    public async Task GetLatestGiftIdeaAsync_ReportsTheNewestRowsOwnChoice()
    {
        // arrange: held first, then shared outright. Sharing outright replaces what was held.
        var exchange = await SeedExchangeAsync();
        await HoldAsync(exchange.Alpha.ParticipantId, "A cast iron skillet");
        await ShareOutrightAsync(exchange.Alpha.ParticipantId, "Actually, a bread book");

        // act
        var latest = await _sut.GetLatestGiftIdeaAsync(exchange.Alpha.ParticipantId);

        // assert
        latest.Ideas.Should().Be("Actually, a bread book");
        latest.HoldUntilAsked.Should().BeFalse();
        latest.HasSharedOutrightBefore.Should().BeTrue();
        latest.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetLatestGiftIdeaAsync_GivenOnlyHeldSubmissions_SaysNothingWentOutOutright()
    {
        // arrange
        var exchange = await SeedExchangeAsync();
        await HoldAsync(exchange.Alpha.ParticipantId, "A cast iron skillet");
        await HoldAsync(exchange.Alpha.ParticipantId, "Actually, a bread book");

        // act
        var latest = await _sut.GetLatestGiftIdeaAsync(exchange.Alpha.ParticipantId);

        // assert: which is what lets the page promise that nobody has seen these.
        latest.Ideas.Should().Be("Actually, a bread book");
        latest.HoldUntilAsked.Should().BeTrue();
        latest.HasSharedOutrightBefore.Should().BeFalse();
    }

    [Fact]
    public async Task GetLatestGiftIdeaAsync_GivenNothingShared_IsEmptyRatherThanNothing()
    {
        // arrange
        var exchange = await SeedExchangeAsync();

        // act
        var latest = await _sut.GetLatestGiftIdeaAsync(exchange.Alpha.ParticipantId);

        // assert
        latest.Ideas.Should().BeEmpty();
        latest.HoldUntilAsked.Should().BeFalse();
        latest.CreatedAt.Should().Be(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task RecordGiftIdeaEnquiryAsync_AskingTwice_KeepsOneRowAndTheFirstDate()
    {
        // arrange
        var exchange = await SeedExchangeAsync();

        // act
        var first = await AskedAboutAsync(exchange.Alpha.ParticipantId, exchange.Beta.ParticipantId);
        var second = await AskedAboutAsync(exchange.Alpha.ParticipantId, exchange.Beta.ParticipantId);

        // assert: asking again is the same standing fact, so nothing is appended and the date stays
        // the first one. Nothing downstream reads it, but a date that moved would be a claim.
        second.RequestedAt.Should().Be(first.RequestedAt);
        second.ReleasedAt.Should().Be(DateTimeOffset.MinValue);

        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeaEnquiries.CountAsync(row =>
                row.AskerParticipantId == exchange.Alpha.ParticipantId))
            .Should().Be(1);
    }

    [Fact]
    public async Task RecordGiftIdeaEnquiryAsync_GivenADifferentSubject_IsADifferentEnquiry()
    {
        // arrange: the same asker, after an organizer edited who they drew.
        var exchange = await SeedExchangeAsync();

        // act
        await AskedAboutAsync(exchange.Alpha.ParticipantId, exchange.Beta.ParticipantId);
        await AskedAboutAsync(exchange.Alpha.ParticipantId, exchange.Gamma.ParticipantId);

        // assert: a row naming a pick they no longer hold releases nothing, which is why the subject
        // is recorded rather than followed back through the draw.
        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeaEnquiries.CountAsync(row =>
                row.AskerParticipantId == exchange.Alpha.ParticipantId))
            .Should().Be(2);
    }

    [Fact]
    public async Task MarkGiftIdeaEnquiryReleasedAsync_StampsTheOneEnquiryAndNobodyElses()
    {
        // arrange
        var exchange = await SeedExchangeAsync();
        await AskedAboutAsync(exchange.Alpha.ParticipantId, exchange.Beta.ParticipantId);
        await AskedAboutAsync(exchange.Beta.ParticipantId, exchange.Gamma.ParticipantId);

        var releasedAt = DateTimeOffset.UtcNow;

        // act
        await _sut.MarkGiftIdeaEnquiryReleasedAsync(new MarkGiftIdeaEnquiryReleasedRequest
        {
            AskerParticipantId = exchange.Alpha.ParticipantId,
            SubjectParticipantId = exchange.Beta.ParticipantId,
            ReleasedAt = releasedAt
        });

        // assert
        await using var context = _contextFactory.CreateDbContext();

        var stamped = await context.GiftIdeaEnquiries
            .Where(row => row.AskerParticipantId == exchange.Alpha.ParticipantId)
            .Select(row => row.ReleasedAt)
            .SingleAsync();

        stamped.Should().BeCloseTo(releasedAt, TimeSpan.FromMilliseconds(1));

        (await context.GiftIdeaEnquiries
                .Where(row => row.AskerParticipantId == exchange.Beta.ParticipantId)
                .Select(row => row.ReleasedAt)
                .SingleAsync())
            .Should().Be(DateTimeOffset.MinValue, "one release is not another");

        // And reading it back reports the stamp, which is what stops a later ask sending the same
        // text again.
        (await AskedAboutAsync(exchange.Alpha.ParticipantId, exchange.Beta.ParticipantId))
            .ReleasedAt.Should().BeCloseTo(releasedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task DeleteParticipantAsync_TakesTheEnquiriesNamingThemInEitherRole()
    {
        // arrange: one enquiry they made, and one somebody else made about them.
        var exchange = await SeedExchangeAsync();
        await AskedAboutAsync(exchange.Alpha.ParticipantId, exchange.Beta.ParticipantId);
        await AskedAboutAsync(exchange.Gamma.ParticipantId, exchange.Alpha.ParticipantId);

        // act
        await _sut.DeleteParticipantAsync(exchange.OrganizerEmail, exchange.HatId, exchange.Alpha.Email);

        // assert: an enquiry about somebody who has left would hold a submission open for a
        // participant who is not there.
        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeaEnquiries.AnyAsync(row =>
                row.AskerParticipantId == exchange.Alpha.ParticipantId
                || row.SubjectParticipantId == exchange.Alpha.ParticipantId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task DeleteHatAsync_TakesEveryEnquiryInItWithIt()
    {
        // arrange
        var exchange = await SeedExchangeAsync();
        await AskedAboutAsync(exchange.Alpha.ParticipantId, exchange.Beta.ParticipantId);
        await AskedAboutAsync(exchange.Beta.ParticipantId, exchange.Gamma.ParticipantId);

        // act
        await _sut.DeleteHatAsync(new DeleteHatRequest
        {
            HatId = exchange.HatId,
            OrganizerEmail = exchange.OrganizerEmail
        });

        // assert: the asker alone reaches all of them, because both participants an enquiry names
        // belong to the exchange that is going.
        await using var context = _contextFactory.CreateDbContext();

        (await context.GiftIdeaEnquiries.AnyAsync(row =>
                exchange.ParticipantIds.Contains(row.AskerParticipantId)))
            .Should().BeFalse();
    }

    /// <summary>One participant having asked for gift ideas about another.</summary>
    private Task<RecordGiftIdeaEnquiryResponse> AskedAboutAsync(Guid askerParticipantId, Guid subjectParticipantId) =>
        _sut.RecordGiftIdeaEnquiryAsync(new RecordGiftIdeaEnquiryRequest
        {
            AskerParticipantId = askerParticipantId,
            SubjectParticipantId = subjectParticipantId
        });

    /// <summary>A submission shared outright, which is what these arrangements mean.</summary>
    private Task<Guid> ShareOutrightAsync(Guid participantId, string ideas) =>
        _sut.AddGiftIdeaAsync(new AddGiftIdeaRequest
        {
            ParticipantId = participantId,
            Ideas = ideas,
            HoldUntilAsked = false
        });

    /// <summary>A submission held back until the person who drew them asks for ideas.</summary>
    private Task<Guid> HoldAsync(Guid participantId, string ideas) =>
        _sut.AddGiftIdeaAsync(new AddGiftIdeaRequest
        {
            ParticipantId = participantId,
            Ideas = ideas,
            HoldUntilAsked = true
        });

    private sealed record SeededParticipant(Guid ParticipantId, string Name, string Email);

    private sealed record SeededExchange(
        Guid HatId,
        string OrganizerEmail,
        SeededParticipant Alpha,
        SeededParticipant Beta,
        SeededParticipant Gamma
    )
    {
        public ImmutableList<Guid> ParticipantIds =>
            [Alpha.ParticipantId, Beta.ParticipantId, Gamma.ParticipantId];

        public ImmutableList<string> Emails => [Alpha.Email, Beta.Email, Gamma.Email];
    }
}
