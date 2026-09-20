using Amazon.Lambda.SQSEvents;
using GiftExchange.Library.Contexts;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The queued half of deleting somebody's data: what goes, and, as much, what is left standing.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DataDeletionTests
{
    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly GiftExchangeProvider _provider;

    private readonly JsonService _jsonService;

    private readonly DataDeletionQueueHandlerService _handlerService;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public DataDeletionTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(_contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _jsonService = serviceProvider.GetRequiredService<JsonService>();
        _handlerService = serviceProvider.GetRequiredService<DataDeletionQueueHandlerService>();
    }

    [Fact]
    public async Task DeleteMyData_RemovesEveryExchangeTheCallerOrganizesAndWhatHangsOffThem()
    {
        // arrange
        var first = await CreateHatAsync();
        var second = await CreateHatAsync(first.OrganizerEmail);

        var participant = await AddParticipantAsync(first);
        var participantId = (await _provider.GetParticipantIdsByEmailAsync(first.HatId))[participant.Person.Email];

        await _provider.AddGiftIdeaAsync(new AddGiftIdeaRequest
        {
            ParticipantId = participantId,
            Ideas = "A good book",
            HoldUntilAsked = false
        });
        await _provider.RecordGiftIdeaEnquiryAsync(new RecordGiftIdeaEnquiryRequest
        {
            AskerParticipantId = participantId,
            SubjectParticipantId = participantId
        });
        await _provider.IssueLeaveTokensAsync(first.HatId);
        await _provider.RecordDoNotAddAsync(new RecordDoNotAddRequest
        {
            Email = "left.already@example.com",
            HatId = first.HatId,
            OrganizerEmail = first.OrganizerEmail,
            BlockOrganizer = false,
            BlockAnywhere = false
        });

        // act
        await DeleteAsync(first.OrganizerEmail, forgetMe: false);

        // assert
        await using var context = await _contextFactory.CreateDbContextAsync();

        (await context.Hats.CountAsync(hat => hat.HatId == first.HatId || hat.HatId == second.HatId)).Should().Be(0);
        (await context.Participants.CountAsync(row => row.HatId == first.HatId || row.HatId == second.HatId)).Should().Be(0);
        (await context.GiftIdeas.CountAsync(idea => idea.ParticipantId == participantId)).Should().Be(0);
        (await context.GiftIdeaEnquiries.CountAsync(row => row.AskerParticipantId == participantId)).Should().Be(0);
        (await context.ParticipantLeaveTokens.CountAsync(token => token.ParticipantId == participantId)).Should().Be(0);
        (await context.DoNotAddToExchange.CountAsync(block => block.HatId == first.HatId)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteMyData_LeavesOtherOrganizersExchangesAlone()
    {
        // arrange
        var mine = await CreateHatAsync();
        var theirs = await CreateHatAsync();

        // act
        await DeleteAsync(mine.OrganizerEmail, forgetMe: false);

        // assert
        var (exists, _) = await _provider.GetHatAsync(theirs.OrganizerEmail, theirs.HatId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteMyData_SparesAnExchangeCreatedAfterTheRequest()
    {
        // arrange
        var old = await CreateHatAsync();
        var requestedAt = DateTimeOffset.UtcNow;
        var newer = await CreateHatAsync(old.OrganizerEmail);

        // act
        await _provider.DeleteMyDataAsync(new DataDeletionMessage
        {
            Email = old.OrganizerEmail,
            ForgetMe = true,
            RequestedAt = requestedAt
        });

        // assert
        (await _provider.GetHatAsync(old.OrganizerEmail, old.HatId)).exists.Should().BeFalse();
        (await _provider.GetHatAsync(newer.OrganizerEmail, newer.HatId)).exists.Should().BeTrue();

        // Still organizing something, so not forgotten.
        (await PersonExistsAsync(old.OrganizerEmail)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteMyData_RemovesPeopleOnlyEverInTheCallersExchanges()
    {
        // arrange
        var mine = await CreateHatAsync();
        var onlyMine = await AddParticipantAsync(mine);

        // act
        await DeleteAsync(mine.OrganizerEmail, forgetMe: false);

        // assert
        (await PersonExistsAsync(onlyMine.Person.Email)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteMyData_KeepsPeopleWhoAreAlsoInSomebodyElsesExchange()
    {
        // arrange
        var mine = await CreateHatAsync();
        var theirs = await CreateHatAsync();

        var shared = await AddParticipantAsync(mine);
        await AddParticipantAsync(theirs, shared.Person.Email, shared.Person.Name);

        // act
        await DeleteAsync(mine.OrganizerEmail, forgetMe: false);

        // assert
        (await PersonExistsAsync(shared.Person.Email)).Should().BeTrue();

        var (_, stored) = await _provider.GetHatAsync(theirs.OrganizerEmail, theirs.HatId);
        stored.Participants.Should().ContainSingle(participant => participant.Person.Email == shared.Person.Email);
    }

    [Fact]
    public async Task DeleteMyData_WithForgetMe_RemovesTheCaller()
    {
        // arrange
        var mine = await CreateHatAsync();
        await AddParticipantAsync(mine, mine.OrganizerEmail, mine.OrganizerName);

        // act
        await DeleteAsync(mine.OrganizerEmail, forgetMe: true);

        // assert
        (await PersonExistsAsync(mine.OrganizerEmail)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteMyData_WithoutForgetMe_KeepsTheCallersName()
    {
        // arrange
        var mine = await CreateHatAsync();

        // act
        await DeleteAsync(mine.OrganizerEmail, forgetMe: false);

        // assert
        var (organizerName, hats) = await _provider.GetHatsAsync(mine.OrganizerEmail);
        organizerName.Should().Be(mine.OrganizerName);
        hats.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteMyData_WithForgetMe_KeepsACallerWhoIsInSomebodyElsesExchange()
    {
        // arrange
        var mine = await CreateHatAsync();
        var theirs = await CreateHatAsync();
        await AddParticipantAsync(theirs, mine.OrganizerEmail, mine.OrganizerName);

        // act
        await DeleteAsync(mine.OrganizerEmail, forgetMe: true);

        // assert
        (await PersonExistsAsync(mine.OrganizerEmail)).Should().BeTrue();

        var (_, stored) = await _provider.GetHatAsync(theirs.OrganizerEmail, theirs.HatId);
        stored.Participants.Should().ContainSingle(participant => participant.Person.Email == mine.OrganizerEmail);
    }

    [Fact]
    public async Task DeleteMyData_RunTwice_IsHarmless()
    {
        // arrange
        var mine = await CreateHatAsync();
        await AddParticipantAsync(mine);

        // act
        await DeleteAsync(mine.OrganizerEmail, forgetMe: true);
        var again = () => DeleteAsync(mine.OrganizerEmail, forgetMe: true);

        // assert
        await again.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteMyData_ForSomebodyTheApplicationDoesNotKnow_DoesNothing()
    {
        var act = () => DeleteAsync("never.seen@example.com", forgetMe: true);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessRecord_DeletesWhatTheQueuedMessageDescribes()
    {
        // arrange
        var mine = await CreateHatAsync();

        var record = new SQSEvent.SQSMessage
        {
            MessageId = "message-one",
            Body = _jsonService.SerializeDefault(new DataDeletionMessage
            {
                Email = mine.OrganizerEmail,
                ForgetMe = true,
                RequestedAt = DateTimeOffset.UtcNow
            })
        };

        // act
        await _handlerService.ProcessRecordAsync(record, new FakeLambdaContext());

        // assert
        (await _provider.GetHatAsync(mine.OrganizerEmail, mine.HatId)).exists.Should().BeFalse();
        (await PersonExistsAsync(mine.OrganizerEmail)).Should().BeFalse();
    }

    private Task DeleteAsync(string email, bool forgetMe) =>
        _provider.DeleteMyDataAsync(new DataDeletionMessage
        {
            Email = email,
            ForgetMe = forgetMe,
            RequestedAt = DateTimeOffset.UtcNow
        });

    private async Task<HatDataModel> CreateHatAsync(string? organizerEmail = null)
    {
        var hat = _hatFaker.Generate();

        if (organizerEmail is not null)
        {
            var (organizerName, _) = await _provider.GetHatsAsync(organizerEmail);
            hat = hat with { OrganizerEmail = organizerEmail, OrganizerName = organizerName };
        }

        await _provider.CreateHatAsync(hat);

        return hat;
    }

    private Task<Participant> AddParticipantAsync(HatDataModel hat, string? email = null, string? name = null)
    {
        var request = _participantFaker.Generate() with { OrganizerEmail = hat.OrganizerEmail, HatId = hat.HatId };

        if (email is not null)
            request = request with { Email = email };

        if (name is not null)
            request = request with { Name = name };

        return _provider.CreateParticipantAsync(request, []);
    }

    private async Task<bool> PersonExistsAsync(string email)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Persons.AnyAsync(person => person.Email == email);
    }
}
