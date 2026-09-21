using System.Data;
using Microsoft.Extensions.Logging;
using NSubstitute;
using GiftExchange.Library.Contexts;
using GiftExchange.Library.Entities;
using Microsoft.EntityFrameworkCore.Storage;

namespace GiftExchange.Library.Tests.HandlerTests;

[Collection(PostgresCollection.Name)]
public class GiftExchangeProviderTests
{
    private readonly GiftExchangeProvider _sut;

    private readonly HatDataModelFaker _hatDataModelFaker;

    private readonly AddParticipantRequestFaker _addParticipantRequestFaker;

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly PostgresFixture _dbFixture;

    public GiftExchangeProviderTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        _hatDataModelFaker = new HatDataModelFaker();
        _addParticipantRequestFaker = new AddParticipantRequestFaker();

        var contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .BuildServiceProvider();

        _sut = serviceProvider.GetRequiredService<GiftExchangeProvider>();
        _contextFactory = contextFactory;
        _dbFixture = dbFixture;
    }

    [Fact]
    public async Task CreateHatAsync_GivenValidPayload_ShouldCreateHat()
    {
        // arrange
        var hat = _hatDataModelFaker.Generate();

        // act
        var result = await _sut.CreateHatAsync(hat);

        // assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrganizerHatsAsync_GivenExistingOrganizerEmail_ShouldReturnHats()
    {
        // arrange
        var hats = _hatDataModelFaker.Generate(2);

        var hatOne = hats[0];
        var hatTwo = hats[1] with
        {
            OrganizerEmail = hatOne.OrganizerEmail,
            OrganizerName = hatOne.OrganizerName
        };

        await _sut.CreateHatAsync(hatOne);
        await _sut.CreateHatAsync(hatTwo);

        // act
        var page = await _sut.GetHatsAsync(FirstPage(hatOne.OrganizerEmail));
        var result = page.Hats;

        // assert
        page.OrganizerName.Should().Be(hatOne.OrganizerName);

        // Compared against anonymous expectations rather than whole records: StatusUpdatedAt is a
        // clock reading taken during the arrange, so it is asserted below for being there rather
        // than restated here as a value this test cannot know.
        result.Should().BeEquivalentTo([
            new { HatId = hatOne.HatId, HatName = hatOne.HatName, Status = HatStatus.InProgress },
            new { HatId = hatTwo.HatId, HatName = hatTwo.HatName, Status = HatStatus.InProgress }
        ]);

        // The list page reads this to say how long each exchange has sat where it is, so it has to
        // survive the projection rather than only the write.
        result.Should().OnlyContain(hat => hat.StatusUpdatedAt != DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task CreateParticipantAsync_GivenValidPayload_ShouldCreateParticipant()
    {
        // arrange
        var hat = await CreateHatAsync();
        var request = ParticipantRequestFor(hat);

        // act
        var result = await _sut.CreateParticipantAsync(request, []);

        // assert
        result.Should().BeOfType<Participant>();
    }

    [Fact]
    public async Task CreateParticipantAsync_GivenDuplicatePayload_ShouldThrowException()
    {
        // arrange
        var hat = await CreateHatAsync();
        var request = ParticipantRequestFor(hat);

        // act
        var firstRequest = await _sut.CreateParticipantAsync(request, []);

        Func<Task> secondRequest = async () => await _sut.CreateParticipantAsync(request, []);

        // assert
        firstRequest.Should().BeOfType<Participant>();
        await secondRequest.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task GetParticipantsAsync_GivenExistingParticipants_ShouldReturnParticipants()
    {
        // arrange
        var hat = await CreateHatAsync();

        foreach (var participant in _addParticipantRequestFaker.Generate(3))
            await _sut.CreateParticipantAsync(ParticipantRequestFor(hat, participant), []);

        // act
        var result = await _sut.GetParticipantsAsync(hat.OrganizerEmail, hat.HatId);

        // assert
        result.Count.Should().Be(3);
    }

    [Fact]
    public async Task CreateParticipantAsync_GivenExistingParticipants_ShouldMakeThemEligible()
    {
        // arrange
        var hat = await CreateHatAsync();

        var first = await _sut.CreateParticipantAsync(ParticipantRequestFor(hat), []);
        var second = await _sut.CreateParticipantAsync(ParticipantRequestFor(hat), [first]);

        // act
        var (exists, stored) = await _sut.GetParticipantAsync(hat.OrganizerEmail, hat.HatId, second.Person.Email);

        // assert: eligibility is stored by id and read back as a person.
        exists.Should().BeTrue();
        stored.EligibleRecipients.Should().ContainSingle().Which.Should().Be(first.Person);
    }

    [Fact]
    public async Task UpdateEligibleRecipientsAsync_GivenAnEmptyList_ClearsThemWithoutFailing()
    {
        // arrange
        var hat = await CreateHatAsync();
        var alpha = await _sut.CreateParticipantAsync(ParticipantRequestFor(hat), []);
        var beta = await _sut.CreateParticipantAsync(ParticipantRequestFor(hat), [alpha]);

        // act: DynamoDB rejects an empty string set outright, so this used to be unrepresentable.
        var act = async () => await _sut.UpdateEligibleRecipientsAsync(
            hat.OrganizerEmail, hat.HatId, beta.Person.Email, []);

        // assert
        await act.Should().NotThrowAsync();

        var (_, stored) = await _sut.GetParticipantAsync(hat.OrganizerEmail, hat.HatId, beta.Person.Email);
        stored.EligibleRecipients.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveParticipantFromEligibleRecipientsAsync_WhenItLeavesSomeoneWithNone_Succeeds()
    {
        // arrange
        var hat = await CreateHatAsync();
        var alpha = await _sut.CreateParticipantAsync(ParticipantRequestFor(hat), []);
        var beta = await _sut.CreateParticipantAsync(ParticipantRequestFor(hat), [alpha]);

        await _sut.UpdateEligibleRecipientsAsync(
            hat.OrganizerEmail, hat.HatId, beta.Person.Email, [alpha.Person.Email]);

        // act: removing Alpha leaves Beta with nobody to draw. Writing that back to DynamoDB meant
        // an empty string set, which it refuses, so the request failed with a 500 partway through.
        var act = async () => await _sut.RemoveParticipantFromEligibleRecipientsAsync(
            hat.OrganizerEmail, hat.HatId, alpha.Person.Email);

        // assert
        await act.Should().NotThrowAsync();

        var (_, stored) = await _sut.GetParticipantAsync(hat.OrganizerEmail, hat.HatId, beta.Person.Email);
        stored.EligibleRecipients.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteParticipantAsync_ClearsAnyPickPointingAtThem()
    {
        // arrange
        var hat = await CreateHatAsync();
        var receiver = await _sut.CreateParticipantAsync(ParticipantRequestFor(hat), []);
        var giver = await _sut.CreateParticipantAsync(ParticipantRequestFor(hat), [receiver]);

        await _sut.UpdateParticipantPickedRecipientAsync(
            hat.OrganizerEmail, hat.HatId, giver.Person.Email, receiver.Person.Email);

        // act: without foreign keys nothing cascades, so the pick has to be cleared explicitly or
        // it dangles at a participant who no longer exists.
        await _sut.DeleteParticipantAsync(hat.OrganizerEmail, hat.HatId, receiver.Person.Email);

        // assert
        var (exists, stored) = await _sut.GetParticipantAsync(hat.OrganizerEmail, hat.HatId, giver.Person.Email);
        exists.Should().BeTrue();
        stored.PickedRecipient.Should().Be(Persons.Empty);
    }

    [Fact]
    public async Task SaveChangesInsideATransaction_DoesNotCreateASavepoint()
    {
        // arrange: UpdateEligibleRecipientsAsync calls SaveChanges inside a transaction that
        // InTransactionAsync already opened. EF's instinct there is to wrap the save in a
        // savepoint so it can undo just that part, and DSQL rejects SAVEPOINT outright. Postgres
        // allows it, so counting the attempts is the only way to see this locally.
        var recorder = new RecordingTransactionInterceptor();

        var provider = new GiftExchangeProvider(
            _dbFixture.CreateContextFactory(recorder),
            Substitute.For<ILogger<GiftExchangeProvider>>());

        var hat = _hatDataModelFaker.Generate();
        await provider.CreateHatAsync(hat);

        var alpha = await provider.CreateParticipantAsync(ParticipantRequestFor(hat), []);
        var beta = await provider.CreateParticipantAsync(ParticipantRequestFor(hat), [alpha]);

        // act
        await provider.UpdateEligibleRecipientsAsync(
            hat.OrganizerEmail, hat.HatId, beta.Person.Email, [alpha.Person.Email]);

        // assert
        recorder.SavepointsCreated.Should().Be(0);

        var (_, stored) = await provider.GetParticipantAsync(hat.OrganizerEmail, hat.HatId, beta.Person.Email);
        stored.EligibleRecipients.Should().ContainSingle().Which.Should().Be(alpha.Person);
    }

    [Fact]
    public async Task SaveChanges_OpensItsOwnImplicitTransactionAtRepeatableRead()
    {
        // arrange: this is the exact path that failed against DSQL. A SaveChanges with more than
        // one statement makes EF open a transaction nobody asked for, at Npgsql's default of READ
        // COMMITTED. Postgres accepts that, so only the recorded level shows whether the
        // interceptor reached it.
        var recorder = new RecordingTransactionInterceptor();
        var contextFactory = _dbFixture.CreateContextFactory(recorder);

        var hat = _hatDataModelFaker.Generate();
        await _sut.CreateHatAsync(hat);

        await using var context = contextFactory.CreateDbContext();

        var firstPersonId = Guid.CreateVersion7();

        var first = new PersonEntity
        {
            PersonId = firstPersonId, Name = "First", Email = $"first.{hat.HatId}@example.com",
            AddedByPersonId = firstPersonId
        };

        var secondPersonId = Guid.CreateVersion7();

        var second = new PersonEntity
        {
            PersonId = secondPersonId, Name = "Second", Email = $"second.{hat.HatId}@example.com",
            AddedByPersonId = secondPersonId
        };

        context.Persons.AddRange(first, second);

        context.Participants.AddRange(
            new ParticipantEntity
            {
                ParticipantId = Guid.CreateVersion7(),
                HatId = hat.HatId,
                PersonId = first.PersonId,
                PickedRecipientParticipantId = Guid.Empty,
                Emoji = "😀"
            },
            new ParticipantEntity
            {
                ParticipantId = Guid.CreateVersion7(),
                HatId = hat.HatId,
                PersonId = second.PersonId,
                PickedRecipientParticipantId = Guid.Empty,
                Emoji = "😀"
            });

        // act
        await context.SaveChangesAsync();

        // assert
        recorder.StartedLevels.Should().NotBeEmpty("a multi-statement SaveChanges opens a transaction");
        recorder.StartedLevels.Should().AllSatisfy(level => level.Should().Be(IsolationLevel.RepeatableRead));
    }

    [Fact]
    public async Task Transactions_AreUpgradedToRepeatableReadEvenWhenSomethingAsksForReadCommitted()
    {
        // arrange: EF opens transactions nobody asked for — SaveChanges wraps a multi-statement
        // batch in one, at Npgsql's default of READ COMMITTED, which DSQL rejects. There is no
        // overload to influence that, so an interceptor upgrades every transaction. Asking for
        // READ COMMITTED explicitly is the observable stand-in for what EF does internally.
        await using var context = _contextFactory.CreateDbContext();

        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted);

        // act
        var connection = context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SHOW transaction_isolation";

        var isolationLevel = (string?)await command.ExecuteScalarAsync();

        // assert
        isolationLevel.Should().Be("repeatable read");
    }

    [Fact]
    public async Task InTransactionAsync_OpensTheTransactionAtRepeatableRead()
    {
        // arrange: DSQL accepts no other isolation level, and Npgsql's default is READ COMMITTED,
        // which it rejects. Postgres tolerates both, so asserting the level is the only way this
        // suite can protect a DSQL-only requirement.
        string? isolationLevel = null;

        // act
        await _sut.InTransactionAsync(async context =>
        {
            var connection = context.Database.GetDbConnection();

            await using var command = connection.CreateCommand();
            command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = "SHOW transaction_isolation";

            isolationLevel = (string?)await command.ExecuteScalarAsync();
        });

        // assert
        isolationLevel.Should().Be("repeatable read");
    }

    /// <summary>
    /// Person is one row per email address for the whole application, so the same address in two
    /// different exchanges is the same person — not a copy of a name in each.
    /// </summary>
    [Fact]
    public async Task TheSameEmail_InTwoHats_IsOnePerson()
    {
        // arrange
        var hatOne = await CreateHatAsync();
        var hatTwo = await CreateHatAsync();

        var shared = _addParticipantRequestFaker.Generate();

        // act
        await _sut.CreateParticipantAsync(ParticipantRequestFor(hatOne, shared), []);
        await _sut.CreateParticipantAsync(ParticipantRequestFor(hatTwo, shared), []);

        // assert
        await using var context = _contextFactory.CreateDbContext();

        var personIds = await context.Participants
            .Where(participant => participant.Person.Email == shared.Email)
            .Select(participant => participant.PersonId)
            .ToListAsync();

        personIds.Should().HaveCount(2, "they are in two exchanges");
        personIds.Distinct().Should().ContainSingle("but they are one person");
    }

    /// <summary>
    /// Two requests can both find nobody at an address and both try to write them. The unique index
    /// on person.email lets one through; the other has to read back what the winner wrote rather
    /// than fail, or creating two exchanges at once would lose one of them.
    /// </summary>
    [Fact]
    public async Task TwoHatsCreatedAtOnce_ForAnUnknownOrganizer_BothSucceed()
    {
        // arrange: one organizer nobody has seen before, so both writes race to introduce them.
        var first = _hatDataModelFaker.Generate();
        var second = _hatDataModelFaker.Generate() with
        {
            OrganizerEmail = first.OrganizerEmail,
            OrganizerName = first.OrganizerName
        };

        // act
        var created = await Task.WhenAll(_sut.CreateHatAsync(first), _sut.CreateHatAsync(second));

        // assert
        created.Should().AllBeEquivalentTo(true);

        var page = await _sut.GetHatsAsync(FirstPage(first.OrganizerEmail));

        page.OrganizerName.Should().Be(first.OrganizerName);
        page.Hats.Select(hat => hat.HatId).Should().BeEquivalentTo([first.HatId, second.HatId]);
    }

    /// <summary>
    /// The sentinel rows are seeded into every database built from the model, so an id that is not
    /// set resolves to a row rather than to nothing.
    /// </summary>
    [Fact]
    public async Task TheSentinelRows_ExistAndAreEmpty()
    {
        await using var context = _contextFactory.CreateDbContext();

        var person = await context.Persons.SingleAsync(candidate => candidate.PersonId == Guid.Empty);
        var hat = await context.Hats.SingleAsync(candidate => candidate.HatId == Guid.Empty);

        person.Name.Should().BeEmpty();
        person.Email.Should().BeEmpty();

        hat.Name.Should().BeEmpty();
        hat.Status.Should().BeEmpty();
        hat.OrganizerPersonId.Should().Be(Guid.Empty, "the sentinel hat is organized by the sentinel person");

        var participant = await context.Participants
            .SingleAsync(candidate => candidate.ParticipantId == Guid.Empty);

        participant.HatId.Should().Be(Guid.Empty);
        participant.PersonId.Should().Be(Guid.Empty);
        participant.PickedRecipientParticipantId.Should().Be(Guid.Empty, "the sentinel participant draws itself");
    }

    /// <summary>
    /// The reason the sentinel participant is worth having: every pick, drawn or not, now names a
    /// row, so following one is an inner join rather than something that has to tolerate a miss.
    /// </summary>
    [Fact]
    public async Task EveryPick_ResolvesToAParticipantRow_EvenBeforeTheHatIsShaken()
    {
        // arrange: nobody has drawn yet, so both picks are the all-zero id.
        var hat = await CreateHatAsync();
        await _sut.CreateParticipantAsync(ParticipantRequestFor(hat), []);
        await _sut.CreateParticipantAsync(ParticipantRequestFor(hat), []);

        await using var context = _contextFactory.CreateDbContext();

        // act: an inner join from every participant in the hat to whoever they drew.
        var resolved = await context.Participants
            .Where(participant => participant.HatId == hat.HatId)
            .Join(
                context.Participants,
                participant => participant.PickedRecipientParticipantId,
                pick => pick.ParticipantId,
                (participant, pick) => new { participant.ParticipantId, PickName = pick.Person.Name })
            .ToListAsync();

        // assert: two rows, not zero — the join found the sentinel for each undrawn pick.
        resolved.Should().HaveCount(2);
        resolved.Should().AllSatisfy(row => row.PickName.Should().BeEmpty());
    }

    /// <summary>
    /// Deleting a hat used to scope only its final statement to the organizer, so a request naming
    /// somebody else's hat stripped its participants and eligibility while leaving the hat itself.
    /// Nothing upstream checks ownership — DeleteHatService passes the request straight through.
    /// </summary>
    [Fact]
    public async Task DeletingAHatYouDoNotOwn_LeavesItAndItsParticipantsAlone()
    {
        // arrange
        var victim = await CreateHatAsync();
        await _sut.CreateParticipantAsync(ParticipantRequestFor(victim), []);

        var attacker = await CreateHatAsync();

        // act: the attacker's own session, pointed at a hat that is not theirs.
        await _sut.DeleteHatAsync(new DeleteHatRequest
        {
            HatId = victim.HatId,
            OrganizerEmail = attacker.OrganizerEmail
        });

        // assert
        var (exists, stored) = await _sut.GetHatAsync(victim.OrganizerEmail, victim.HatId);

        exists.Should().BeTrue("the hat is not theirs to delete");
        stored.Participants.Should().ContainSingle("nor are its participants");
    }

    /// <summary>
    /// The same guard protects the sentinel rows, which the all-zero hat id would otherwise reach.
    /// </summary>
    [Fact]
    public async Task DeletingTheAllZeroHatId_LeavesTheSentinelParticipantAlone()
    {
        // arrange
        var hat = await CreateHatAsync();

        // act
        await _sut.DeleteHatAsync(new DeleteHatRequest
        {
            HatId = Guid.Empty,
            OrganizerEmail = hat.OrganizerEmail
        });

        // assert
        await using var context = _contextFactory.CreateDbContext();

        (await context.Participants.AnyAsync(candidate => candidate.ParticipantId == Guid.Empty))
            .Should().BeTrue();
        (await context.Hats.AnyAsync(candidate => candidate.HatId == Guid.Empty))
            .Should().BeTrue();
    }

    /// <summary>
    /// A hat whose organizer is a real person, joined to that person, is an inner join that still
    /// finds them — the sentinel sits alongside real rows without being one of them.
    /// </summary>
    [Fact]
    public async Task TheSentinelHat_IsNotReturnedAmongARealOrganizersHats()
    {
        // arrange
        var hat = await CreateHatAsync();

        // act
        var hats = (await _sut.GetHatsAsync(FirstPage(hat.OrganizerEmail))).Hats;

        // assert
        hats.Select(metadata => metadata.HatId).Should().NotContain(Guid.Empty);
        hats.Select(metadata => metadata.HatId).Should().Contain(hat.HatId);
    }

    /// <summary>
    /// The sentinel person holds the empty address, so an empty one has to be refused rather than
    /// looked up: it would match, and its hats are the sentinel hat.
    /// </summary>
    [Fact]
    public async Task AnEmptyOrganizerEmail_DoesNotReachTheSentinelHat()
    {
        var page = await _sut.GetHatsAsync(FirstPage(string.Empty));

        page.OrganizerName.Should().BeEmpty();
        page.Hats.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    /// <summary>
    /// Hats created in the same instant -- which a copy can manage -- still land on exactly one page
    /// each, because the hat id breaks the tie.
    /// </summary>
    [Fact]
    public async Task GetHatsAsync_GivenHatsCreatedInTheSameInstant_PagesAreDisjointAndComplete()
    {
        // arrange
        var first = await CreateHatAsync();
        var others = _hatDataModelFaker.Generate(4)
            .Select(hat => hat with { OrganizerEmail = first.OrganizerEmail, OrganizerName = first.OrganizerName })
            .ToList();

        foreach (var hat in others)
            await _sut.CreateHatAsync(hat);

        var hatIds = others.Select(hat => hat.HatId).Append(first.HatId).ToList();

        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            var instant = DateTimeOffset.UtcNow;
            await context.Hats
                .Where(hat => hatIds.Contains(hat.HatId))
                .ExecuteUpdateAsync(setters => setters.SetProperty(hat => hat.CreatedAt, instant));
        }

        // act
        var pages = new List<GetHatsPageResponse>();
        for (var page = 1; page <= 3; page++)
            pages.Add(await _sut.GetHatsAsync(new GetHatsPageRequest
            {
                OrganizerEmail = first.OrganizerEmail,
                Page = page,
                PageSize = 2
            }));

        // assert
        pages.Select(page => page.Hats.Count).Should().Equal(2, 2, 1);
        pages.Should().AllSatisfy(page => page.TotalCount.Should().Be(5));
        pages.SelectMany(page => page.Hats).Select(hat => hat.HatId)
            .Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(hatIds);
    }

    [Fact]
    public async Task GetHatsAsync_GivenAPagePastTheEnd_ReturnsNoHatsButTheRealCount()
    {
        // arrange
        var hat = await CreateHatAsync();

        // act
        var page = await _sut.GetHatsAsync(new GetHatsPageRequest
        {
            OrganizerEmail = hat.OrganizerEmail,
            Page = 2,
            PageSize = 5
        });

        // assert
        page.Hats.Should().BeEmpty();
        page.TotalCount.Should().Be(1);
        page.OrganizerName.Should().Be(hat.OrganizerName);
    }

    [Fact]
    public async Task TheSentinelHat_IsNotReadableAsAHat()
    {
        var (exists, _) = await _sut.GetHatAsync("anyone@example.com", Guid.Empty);

        exists.Should().BeFalse();
    }

    /// <summary>
    /// The sentinel is a mutable row like any other, so the one address that would match it is
    /// refused before a rename can reach it.
    /// </summary>
    [Fact]
    public async Task RenamingTheEmptyAddress_IsRefusedRatherThanRenamingTheSentinel()
    {
        var rename = async () => await _sut.RenamePersonAsync(new RenamePersonRequest
        {
            Email = string.Empty,
            Name = "Not The Sentinel",
            RequestedByEmail = string.Empty
        });

        await rename.Should().ThrowAsync<ArgumentException>();

        await using var context = _contextFactory.CreateDbContext();

        var person = await context.Persons.SingleAsync(candidate => candidate.PersonId == Guid.Empty);

        person.Name.Should().BeEmpty();
    }

    /// <summary>
    /// A copy remembers where it came from. Nothing reads this yet; it is the record a rule like
    /// "nobody draws the same person two years running" would need, and it can only be captured at
    /// the moment the copy is made.
    /// </summary>
    [Fact]
    public async Task CopyHatAsync_RecordsTheHatItWasCopiedFrom()
    {
        // arrange
        var source = await CreateHatAsync();
        await _sut.CreateParticipantAsync(ParticipantRequestFor(source), []);

        var copy = _hatDataModelFaker.Generate() with
        {
            OrganizerEmail = source.OrganizerEmail,
            OrganizerName = source.OrganizerName
        };

        // act
        var copied = await _sut.CopyHatAsync(CopyOf(source.HatId, copy));

        // assert
        copied.Should().BeTrue();

        await using var context = _contextFactory.CreateDbContext();

        var stored = await context.Hats.SingleAsync(hat => hat.HatId == copy.HatId);

        stored.CopiedFromHatId.Should().Be(source.HatId);
    }

    /// <summary>
    /// A hat made from scratch points at the sentinel, so "not a copy" is a row rather than a null
    /// and following the column is an inner join.
    /// </summary>
    [Fact]
    public async Task CreateHatAsync_MarksTheHatAsNotACopy()
    {
        // arrange
        var hat = await CreateHatAsync();

        // act
        await using var context = _contextFactory.CreateDbContext();

        var provenance = await context.Hats
            .Where(candidate => candidate.HatId == hat.HatId)
            .Join(
                context.Hats,
                candidate => candidate.CopiedFromHatId,
                source => source.HatId,
                (candidate, source) => new { candidate.CopiedFromHatId, SourceName = source.Name })
            .SingleAsync();

        // assert: the join finds the sentinel rather than nothing.
        provenance.CopiedFromHatId.Should().Be(Guid.Empty);
        provenance.SourceName.Should().BeEmpty();
    }

    /// <summary>
    /// Copying a copy records the hat it came from, not the one at the head of the chain, so the
    /// chain stays walkable one link at a time.
    /// </summary>
    [Fact]
    public async Task CopyingACopy_RecordsTheImmediateSource()
    {
        // arrange
        var first = await CreateHatAsync();

        var second = _hatDataModelFaker.Generate() with
        {
            OrganizerEmail = first.OrganizerEmail, OrganizerName = first.OrganizerName
        };
        await _sut.CopyHatAsync(CopyOf(first.HatId, second));

        var third = _hatDataModelFaker.Generate() with
        {
            OrganizerEmail = first.OrganizerEmail, OrganizerName = first.OrganizerName
        };

        // act
        await _sut.CopyHatAsync(CopyOf(second.HatId, third));

        // assert
        await using var context = _contextFactory.CreateDbContext();

        (await context.Hats.SingleAsync(hat => hat.HatId == third.HatId))
            .CopiedFromHatId.Should().Be(second.HatId);
    }

    /// <summary>
    /// Deleting the source would otherwise leave the copy pointing at an exchange that no longer
    /// exists. Nothing cascades in DSQL, so the provider clears it, as it does a pick.
    /// </summary>
    [Fact]
    public async Task DeletingASourceHat_LeavesItsCopyMarkedAsNotACopy()
    {
        // arrange
        var source = await CreateHatAsync();

        var copy = _hatDataModelFaker.Generate() with
        {
            OrganizerEmail = source.OrganizerEmail, OrganizerName = source.OrganizerName
        };
        await _sut.CopyHatAsync(CopyOf(source.HatId, copy));

        // act
        await _sut.DeleteHatAsync(new DeleteHatRequest
        {
            HatId = source.HatId,
            OrganizerEmail = source.OrganizerEmail
        });

        // assert
        await using var context = _contextFactory.CreateDbContext();

        (await context.Hats.AnyAsync(hat => hat.HatId == source.HatId)).Should().BeFalse();
        (await context.Hats.SingleAsync(hat => hat.HatId == copy.HatId))
            .CopiedFromHatId.Should().Be(Guid.Empty, "the exchange it came from is gone");
    }

    /// <summary>
    /// Creating a hat is where it takes its first status, so the two timestamps agree exactly
    /// rather than by a hair -- one reading of the clock writes both.
    /// </summary>
    [Fact]
    public async Task CreateHatAsync_StampsStatusUpdatedAtWithTheCreationTime()
    {
        // arrange
        var hat = await CreateHatAsync();

        // act
        await using var context = _contextFactory.CreateDbContext();

        var stored = await context.Hats.SingleAsync(candidate => candidate.HatId == hat.HatId);

        // assert
        stored.StatusUpdatedAt.Should().Be(stored.CreatedAt);
        stored.StatusUpdatedAt.Should().NotBe(DateTimeOffset.MinValue);
    }

    /// <summary>
    /// The point of the column: every route that moves the status moves this with it, so how long
    /// a hat has sat where it is can be read rather than inferred.
    /// </summary>
    [Fact]
    public async Task UpdateHatStatusAsync_MovesStatusUpdatedAt()
    {
        // arrange
        var hat = await CreateHatAsync();
        var createdAt = await StatusUpdatedAtOf(hat.HatId);

        // act
        await _sut.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.ReadyForAssignment);

        // assert
        (await StatusUpdatedAtOf(hat.HatId)).Should().BeAfter(createdAt);
    }

    /// <summary>
    /// Queuing invitations is a status change like any other, and it writes the same reading into
    /// both timestamps rather than calling the clock twice.
    /// </summary>
    [Fact]
    public async Task MarkInvitationsAsQueuedAsync_MovesStatusUpdatedAtWithTheStatus()
    {
        // arrange
        var hat = await CreateHatAsync();
        await _sut.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.NamesAssigned);

        // act
        await _sut.MarkInvitationsAsQueuedAsync(hat.OrganizerEmail, hat.HatId, "203.0.113.7");

        // assert
        await using var context = _contextFactory.CreateDbContext();

        var stored = await context.Hats.SingleAsync(candidate => candidate.HatId == hat.HatId);

        stored.Status.Should().Be(HatStatus.InvitationsSent);
        stored.StatusUpdatedAt.Should().Be(stored.InvitationsQueuedAt);
    }

    /// <summary>
    /// The scheduled transition runs without an organizer present, and stamps the column all the
    /// same.
    /// </summary>
    [Fact]
    public async Task TryTransitionHatToCooledOffAsync_MovesStatusUpdatedAt()
    {
        // arrange
        var hat = await CreateHatAsync();
        await _sut.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.NamesAssigned);
        await _sut.MarkInvitationsAsQueuedAsync(hat.OrganizerEmail, hat.HatId, "203.0.113.7");

        var queuedAt = await StatusUpdatedAtOf(hat.HatId);

        // act
        await _sut.TryTransitionHatToCooledOffAsync(hat.OrganizerEmail, hat.HatId);

        // assert
        (await StatusUpdatedAtOf(hat.HatId)).Should().BeAfter(queuedAt);
    }

    /// <summary>
    /// Resetting puts the status back to the beginning, which is a change like any other.
    /// </summary>
    [Fact]
    public async Task ResetHatAsync_MovesStatusUpdatedAt()
    {
        // arrange
        var hat = await CreateHatAsync();
        await _sut.UpdateHatStatusAsync(hat.OrganizerEmail, hat.HatId, HatStatus.NamesAssigned);

        var assignedAt = await StatusUpdatedAtOf(hat.HatId);

        // act
        var wasReset = await _sut.ResetHatAsync(new ResetHatRequest
        {
            HatId = hat.HatId,
            OrganizerEmail = hat.OrganizerEmail
        });

        // assert
        wasReset.Should().BeTrue();
        (await StatusUpdatedAtOf(hat.HatId)).Should().BeAfter(assignedAt);
    }

    private async Task<DateTimeOffset> StatusUpdatedAtOf(Guid hatId)
    {
        await using var context = _contextFactory.CreateDbContext();

        return (await context.Hats.SingleAsync(hat => hat.HatId == hatId)).StatusUpdatedAt;
    }

    private static GetHatsPageRequest FirstPage(string organizerEmail) =>
        new() { OrganizerEmail = organizerEmail, Page = 1, PageSize = 100 };

    /// <summary>
    /// Participants belong to a hat. DynamoDB tolerated orphans; a relational schema does not, so
    /// the hat has to exist first.
    /// </summary>
    private async Task<HatDataModel> CreateHatAsync()
    {
        var hat = _hatDataModelFaker.Generate();
        await _sut.CreateHatAsync(hat);
        return hat;
    }

    private AddParticipantRequest ParticipantRequestFor(HatDataModel hat, AddParticipantRequest? request = null)
    {
        request ??= _addParticipantRequestFaker.Generate();

        return request with { HatId = hat.HatId, OrganizerEmail = hat.OrganizerEmail };
    }

    /// <summary>
    /// A plain copy: everybody carried over, and last year's picks still eligible.
    /// </summary>
    /// <remarks>
    /// The two rules that leave people out are covered by CopyHatTests, which drives the endpoint
    /// and so exercises the do-not-add lookup that fills the second of them in. What these tests
    /// are about is the copying itself.
    /// </remarks>
    private static CopyHatDataRequest CopyOf(Guid sourceHatId, HatDataModel newHat) =>
        new()
        {
            SourceHatId = sourceHatId,
            NewHat = newHat,
            ExcludePreviousRecipients = false,
            RefusedEmails = []
        };
}
