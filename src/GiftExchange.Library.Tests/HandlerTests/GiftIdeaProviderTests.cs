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
        await _sut.AddGiftIdeaAsync(exchange.Alpha.ParticipantId, "A cast iron skillet");
        await _sut.AddGiftIdeaAsync(exchange.Alpha.ParticipantId, "Actually, a bread book");

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
        await _sut.AddGiftIdeaAsync(exchange.Alpha.ParticipantId, "A scarf");

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
            await _sut.AddGiftIdeaAsync(participantId, "Something");

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
