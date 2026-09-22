using GiftExchange.Library.Contexts;
using GiftExchange.Library.Entities;
using Microsoft.Data.Sqlite;

namespace GiftExchange.Library.Tests.EntityTests;

/// <summary>
/// Proves the entities actually materialise, which model building alone does not. SQLite in
/// memory stands in for DSQL here: it is a real relational provider, so it exercises EF's
/// materialiser and relationship fixup, but it is not a substitute for testing against a cluster.
/// The point is the mapping mechanics, not DSQL semantics.
/// </summary>
public class EntityRoundTripTests : IDisposable
{
    private readonly SqliteConnection _connection;

    private readonly DbContextOptions<GiftExchangeDbContext> _options;

    public EntityRoundTripTests()
    {
        // An in-memory SQLite database lives only as long as its connection is open.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<GiftExchangeDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new GiftExchangeDbContext(_options);
        context.Database.EnsureCreated();
    }

    private GiftExchangeDbContext CreateContext() => new(_options);

    [Fact]
    public async Task Entities_WithRequiredMembers_RoundTripThroughEfCore()
    {
        var hatId = Guid.CreateVersion7();
        var organizerPersonId = Guid.CreateVersion7();
        var otherPersonId = Guid.CreateVersion7();
        var organizerParticipantId = Guid.CreateVersion7();
        var otherParticipantId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            context.Persons.AddRange(
                new PersonEntity
                {
                    PersonId = organizerPersonId, Name = "Organizer", Email = "organizer@example.com",
                    AddedByPersonId = organizerPersonId
                },
                new PersonEntity
                {
                    PersonId = otherPersonId, Name = "Someone Else", Email = "someone@example.com",
                    AddedByPersonId = otherPersonId
                });

            context.Hats.Add(new HatEntity
            {
                HatId = hatId,
                OrganizerPersonId = organizerPersonId,
                Name = "Family Christmas",
                NameNormalized = "family christmas",
                Status = HatStatus.InProgress,
                StatusUpdatedAt = DateTimeOffset.UtcNow,
                AdditionalInformation = string.Empty,
                PriceRange = string.Empty,
                InvitationsQueuedAt = DateTimeOffset.MinValue,
                InvitationsSentFromIp = string.Empty,
                CreatedAt = DateTimeOffset.UtcNow,
                CopiedFromHatId = Guid.Empty,
                ExchangeDate = new DateOnly(2026, 12, 25),
                ClosePromptSentAt = DateTimeOffset.MinValue
            });

            context.Participants.AddRange(
                new ParticipantEntity
                {
                    ParticipantId = organizerParticipantId,
                    HatId = hatId,
                    PersonId = organizerPersonId,
                    PickedRecipientParticipantId = Guid.Empty,
                    Emoji = "😀"
                },
                new ParticipantEntity
                {
                    ParticipantId = otherParticipantId,
                    HatId = hatId,
                    PersonId = otherPersonId,
                    PickedRecipientParticipantId = Guid.Empty,
                    Emoji = "😀"
                });

            context.ParticipantEligibleRecipients.Add(new ParticipantEligibleRecipientEntity
            {
                ParticipantEligibleRecipientId = Guid.CreateVersion7(),
                ParticipantId = organizerParticipantId,
                EligibleParticipantId = otherParticipantId
            });

            await context.SaveChangesAsync();
        }

        // A fresh context, so this reads from the database rather than the change tracker.
        await using (var context = CreateContext())
        {
            var hat = await context.Hats
                .Include(h => h.Organizer)
                .Include(h => h.Participants).ThenInclude(p => p.Person)
                .Include(h => h.Participants).ThenInclude(p => p.EligibleRecipients)
                .SingleAsync(h => h.HatId == hatId);

            hat.Name.Should().Be("Family Christmas");
            hat.Organizer.Name.Should().Be("Organizer");
            // Nothing is null, so "not queued" is a value like any other.
            hat.InvitationsQueuedAt.Should().Be(DateTimeOffset.MinValue);
            hat.InvitationsSentFromIp.Should().BeEmpty();
            hat.ExchangeDate.Should().Be(new DateOnly(2026, 12, 25));
            hat.ClosePromptSentAt.Should().Be(DateTimeOffset.MinValue);
            hat.Participants.Should().HaveCount(2);
            hat.Participants
                .Single(p => p.ParticipantId == organizerParticipantId)
                .EligibleRecipients.Should().ContainSingle();
        }
    }

    /// <summary>
    /// The organizer is a participant like anyone else, and both roles resolve to the same row in
    /// person rather than to two copies of a name.
    /// </summary>
    [Fact]
    public async Task Organizer_AndTheirParticipantRow_ShareOnePerson()
    {
        var hatId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var participantId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            context.Persons.Add(new PersonEntity
            {
                PersonId = personId, Name = "Organizer", Email = "organizer@example.com",
                AddedByPersonId = personId
            });
            context.Hats.Add(NewHat(hatId, personId, "Shared Person Hat", HatStatus.InProgress));
            context.Participants.Add(new ParticipantEntity
            {
                ParticipantId = participantId,
                HatId = hatId,
                PersonId = personId,
                PickedRecipientParticipantId = Guid.Empty,
                Emoji = "😀"
            });

            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var person = await context.Persons
                .Include(p => p.OrganizedHats)
                .Include(p => p.Participations)
                .SingleAsync(p => p.PersonId == personId);

            person.OrganizedHats.Should().ContainSingle().Which.HatId.Should().Be(hatId);
            person.Participations.Should().ContainSingle().Which.ParticipantId.Should().Be(participantId);
        }
    }

    /// <summary>
    /// A pick is a participant id with no navigation behind it, so it is stored and read as a plain
    /// value. Resolving it to a name is the provider's job, against the rest of the hat.
    /// </summary>
    [Fact]
    public async Task PickedRecipient_IsStoredAsAParticipantId()
    {
        var hatId = Guid.CreateVersion7();
        var giverId = Guid.CreateVersion7();
        var receiverId = Guid.CreateVersion7();
        var giverPersonId = Guid.CreateVersion7();
        var receiverPersonId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            context.Persons.AddRange(
                new PersonEntity { PersonId = giverPersonId, Name = "Giver", Email = "giver@example.com", AddedByPersonId = giverPersonId },
                new PersonEntity { PersonId = receiverPersonId, Name = "Receiver", Email = "receiver@example.com", AddedByPersonId = receiverPersonId });

            context.Hats.Add(NewHat(hatId, giverPersonId, "Shaken Hat", HatStatus.NamesAssigned));

            context.Participants.AddRange(
                new ParticipantEntity
                {
                    ParticipantId = receiverId,
                    HatId = hatId,
                    PersonId = receiverPersonId,
                    PickedRecipientParticipantId = Guid.Empty,
                    Emoji = "😀"
                },
                new ParticipantEntity
                {
                    ParticipantId = giverId,
                    HatId = hatId,
                    PersonId = giverPersonId,
                    PickedRecipientParticipantId = receiverId,
                    Emoji = "😀"
                });

            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var participants = await context.Participants
                .Include(p => p.Person)
                .Where(p => p.HatId == hatId)
                .ToListAsync();

            var giver = participants.Single(p => p.ParticipantId == giverId);

            giver.PickedRecipientParticipantId.Should().Be(receiverId);
            participants
                .Single(p => p.ParticipantId == giver.PickedRecipientParticipantId)
                .Person.Name.Should().Be("Receiver");
        }
    }

    /// <summary>
    /// The all-zero id points at nobody, which is what an unshaken hat looks like. It has to be
    /// storable, which is exactly why picked_recipient_participant_id carries no foreign key.
    /// </summary>
    [Fact]
    public async Task ParticipantWithNoPick_StoresTheAllZeroId()
    {
        var hatId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var participantId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            context.Persons.Add(new PersonEntity
            {
                PersonId = personId, Name = "Undrawn", Email = "undrawn@example.com",
                AddedByPersonId = personId
            });
            context.Hats.Add(NewHat(hatId, personId, "Unshaken Hat", HatStatus.InProgress));
            context.Participants.Add(new ParticipantEntity
            {
                ParticipantId = participantId,
                HatId = hatId,
                PersonId = personId,
                PickedRecipientParticipantId = Guid.Empty,
                Emoji = "😀"
            });

            var save = async () => await context.SaveChangesAsync();

            await save.Should().NotThrowAsync();
        }

        await using (var context = CreateContext())
        {
            var participant = await context.Participants.SingleAsync(p => p.ParticipantId == participantId);

            participant.PickedRecipientParticipantId.Should().Be(Guid.Empty);
        }
    }

    [Fact]
    public async Task TrackedEntity_CanBeMutatedAndSaved()
    {
        var hatId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            context.Persons.Add(new PersonEntity
            {
                PersonId = personId, Name = "Organizer", Email = "organizer@example.com",
                AddedByPersonId = personId
            });
            context.Hats.Add(NewHat(hatId, personId, "Mutable Hat", HatStatus.InProgress));

            await context.SaveChangesAsync();
        }

        // This is why the properties are `set` rather than `init`: EF's update path is to load,
        // mutate, and save. Init-only properties would not compile here.
        await using (var context = CreateContext())
        {
            var hat = await context.Hats.SingleAsync(h => h.HatId == hatId);
            hat.Status = HatStatus.Closed;
            hat.StatusUpdatedAt = DateTimeOffset.UtcNow;
            hat.InvitationsQueuedAt = DateTimeOffset.UtcNow;
            hat.InvitationsSentFromIp = "203.0.113.7";
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var hat = await context.Hats.SingleAsync(h => h.HatId == hatId);
            hat.Status.Should().Be(HatStatus.Closed);
            hat.StatusUpdatedAt.Should().NotBe(DateTimeOffset.MinValue);
            hat.InvitationsQueuedAt.Should().NotBe(DateTimeOffset.MinValue);
            hat.InvitationsSentFromIp.Should().Be("203.0.113.7");
        }
    }

    /// <summary>
    /// Every field is stated, because the table has no defaults to fall back on.
    /// </summary>
    private static HatEntity NewHat(Guid hatId, Guid organizerPersonId, string name, string status) =>
        new()
        {
            HatId = hatId,
            OrganizerPersonId = organizerPersonId,
            Name = name,
            NameNormalized = name.ToLowerInvariant(),
            Status = status,
            StatusUpdatedAt = DateTimeOffset.UtcNow,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            InvitationsQueuedAt = DateTimeOffset.MinValue,
            InvitationsSentFromIp = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            CopiedFromHatId = Guid.Empty,
            ExchangeDate = DateOnly.MinValue,
            ClosePromptSentAt = DateTimeOffset.MinValue
        };

    public void Dispose() => _connection.Dispose();
}
