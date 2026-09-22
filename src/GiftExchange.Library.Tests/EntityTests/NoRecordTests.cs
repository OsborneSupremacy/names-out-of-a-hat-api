using System.Text.RegularExpressions;
using GiftExchange.Library.Contexts;
using GiftExchange.Library.Entities;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GiftExchange.Library.Tests.EntityTests;

/// <summary>
/// The sentinel rows are written twice: by Liquibase into the real database, and by <c>HasData</c>
/// into the model, which is what seeds the databases this suite builds. Two spellings of one fact
/// drift, so these hold them together — and hold both to the rule that a sentinel is empty.
/// </summary>
public partial class NoRecordTests
{
    /// <summary>The changesets that insert the sentinel rows.</summary>
    private static readonly ImmutableList<string> SeedChangeSets =
        ["person--0002", "hat--0002", "participant--0002"];

    [Fact]
    public void SentinelPerson_IsEmptyThroughout()
    {
        var person = NoRecord.Person();

        person.PersonId.Should().Be(Guid.Empty);
        person.Name.Should().BeEmpty();
        person.Email.Should().BeEmpty();
        // Introduced by itself, which is how this schema spells "nobody introduced them".
        person.AddedByPersonId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void SentinelHat_IsEmptyThroughout()
    {
        var hat = NoRecord.Hat();

        hat.HatId.Should().Be(Guid.Empty);
        // Self-consistent: following the sentinel hat's organizer reaches the sentinel person
        // rather than nothing at all.
        hat.OrganizerPersonId.Should().Be(Guid.Empty);
        hat.Name.Should().BeEmpty();
        hat.NameNormalized.Should().BeEmpty();
        hat.Status.Should().BeEmpty();
        hat.StatusUpdatedAt.Should().Be(DateTimeOffset.MinValue);
        hat.AdditionalInformation.Should().BeEmpty();
        hat.PriceRange.Should().BeEmpty();
        hat.InvitationsQueuedAt.Should().Be(DateTimeOffset.MinValue);
        hat.InvitationsSentFromIp.Should().BeEmpty();
        hat.CreatedAt.Should().Be(DateTimeOffset.MinValue);
        hat.CopiedFromHatId.Should().Be(Guid.Empty);
        hat.ExchangeDate.Should().Be(DateOnly.MinValue);
        hat.ClosePromptSentAt.Should().Be(DateTimeOffset.MinValue);
    }

    [Fact]
    public void SentinelParticipant_IsEmptyThroughout()
    {
        var participant = NoRecord.Participant();

        participant.ParticipantId.Should().Be(Guid.Empty);
        participant.HatId.Should().Be(Guid.Empty);
        participant.PersonId.Should().Be(Guid.Empty);
        // It draws itself, which is what makes following any unshaken pick an inner join.
        participant.PickedRecipientParticipantId.Should().Be(Guid.Empty);
        // Nobody has a face, which is the point: this row stands for not taking part.
        participant.Emoji.Should().BeEmpty();
    }

    /// <summary>
    /// A fresh instance every time, so a caller that mutates what it is handed cannot leave a
    /// sentinel that is no longer empty behind for the next one.
    /// </summary>
    [Fact]
    public void SentinelFactories_HandOutDistinctInstances()
    {
        NoRecord.Person().Should().NotBeSameAs(NoRecord.Person());
        NoRecord.Hat().Should().NotBeSameAs(NoRecord.Hat());
        NoRecord.Participant().Should().NotBeSameAs(NoRecord.Participant());
    }

    [Fact]
    public void EverySentinelValueInTheMigrations_IsEmpty()
    {
        foreach (var changeSet in SeedChangeSets)
        {
            var values = ParseInsertedValues(changeSet);

            values.Should().NotBeEmpty($"{changeSet} should insert a sentinel row");

            values
                .Where(value => value != "''" && value != AllZeroId && !IsMinimumTimestamp(value))
                .Should().BeEmpty($"every column {changeSet} inserts should be empty, the all-zero id, or the minimum timestamp");
        }
    }

    /// <summary>
    /// The migrations and the model have to describe the same row.
    ///
    /// A seed cannot name a column that did not exist when it ran, so a column added later counts
    /// too — but only because the schema forbids both nulls and defaults, which leaves a backfill
    /// as the one way to give the existing rows, sentinel included, a value. That is asserted
    /// rather than assumed.
    ///
    /// Without this, a column added to the table and forgotten everywhere else would leave the real
    /// sentinel narrower than the one the suite builds from the model.
    /// </summary>
    [Theory]
    [InlineData("person--0002", "person")]
    [InlineData("hat--0002", "hat")]
    [InlineData("participant--0002", "participant")]
    public void TheMigrationSeed_AccountsForEveryColumnTheModelMaps(string changeSet, string table)
    {
        var entity = BuildModel()
            .GetEntityTypes()
            .Single(candidate => candidate.GetTableName() == table);

        var storeObject = StoreObjectIdentifier.Table(table, entity.GetSchema());

        var mapped = entity.GetProperties()
            .Select(property => property.GetColumnName(storeObject))
            .Where(column => column is not null)
            .ToImmutableSortedSet()!;

        var seeded = ParseInsertedColumns(changeSet);
        var addedLater = ColumnsAddedAfter(changeSet, table);

        seeded.Union(addedLater).Should().BeEquivalentTo(mapped);

        // Every named column gets a value, which is what makes the row insertable into a table with
        // no defaults to fall back on.
        ParseInsertedValues(changeSet).Should().HaveCount(seeded.Count);

        addedLater.Should().BeSubsetOf(
            ColumnsBackfilled(table),
            "a column added after the sentinel was seeded can only reach it through a backfill");
    }

    /// <summary>Columns an ALTER adds to this table after the seed has run.</summary>
    private static ImmutableSortedSet<string> ColumnsAddedAfter(string seedChangeSet, string table)
    {
        var seedIndex = Migrations.Statements.FindIndex(statement => statement.ChangeSetId == seedChangeSet);

        seedIndex.Should().BeGreaterThanOrEqualTo(0, $"{seedChangeSet} should be in the changelog");

        return MatchingTable(Migrations.Statements.Skip(seedIndex + 1), AddedColumn(), table);
    }

    /// <summary>Columns an UPDATE assigns a value to across every row of this table.</summary>
    private static ImmutableSortedSet<string> ColumnsBackfilled(string table) =>
        MatchingTable(Migrations.Statements, BackfilledColumn(), table);

    private static ImmutableSortedSet<string> MatchingTable(
        IEnumerable<Migrations.Statement> statements,
        Regex pattern,
        string table
    ) =>
        statements
            .Select(statement => pattern.Match(statement.Sql))
            .Where(match => match.Success && match.Groups[1].Value.Equals(table, StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Groups[2].Value)
            .ToImmutableSortedSet();

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<GiftExchangeDbContext>()
            .UseNpgsql("Host=localhost;Database=placeholder")
            .Options;

        using var context = new GiftExchangeDbContext(options);
        return context.Model;
    }

    private const string AllZeroId = "'00000000-0000-0000-0000-000000000000'";

    private static bool IsMinimumTimestamp(string value) =>
        value.StartsWith("'0001-01-01", StringComparison.Ordinal);

    /// <summary>The column names from the INSERT list of a seed changeset.</summary>
    private static ImmutableSortedSet<string> ParseInsertedColumns(string changeSet)
    {
        var columns = ColumnList().Match(SeedSql(changeSet));

        columns.Success.Should().BeTrue($"{changeSet} should name the columns it inserts");

        return Split(columns.Groups[1].Value).ToImmutableSortedSet();
    }

    /// <summary>The quoted values from the VALUES clause of a seed changeset.</summary>
    private static ImmutableList<string> ParseInsertedValues(string changeSet)
    {
        var values = ValuesClause().Match(SeedSql(changeSet));

        values.Success.Should().BeTrue($"{changeSet} should hold one INSERT with a VALUES clause");

        return Split(values.Groups[1].Value).ToImmutableList();
    }

    private static string SeedSql(string changeSet) =>
        Migrations.Statements.Single(statement => statement.ChangeSetId == changeSet).Sql;

    private static IEnumerable<string> Split(string clause) =>
        clause
            .Split(',')
            .Select(item => item.Trim())
            .Where(item => item.Length > 0);

    /// <summary>The parenthesised group between the table name and the VALUES keyword.</summary>
    [GeneratedRegex(@"INSERT\s+INTO\s+\w+\s*\(([^)]*)\)\s*VALUES", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ColumnList();

    /// <summary>
    /// The parenthesised group following the VALUES keyword. Anchoring on the keyword is what keeps
    /// the column list, which is parenthesised too, out of the match.
    /// </summary>
    [GeneratedRegex(@"VALUES\s*\(([^)]*)\)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ValuesClause();

    [GeneratedRegex(@"ALTER\s+TABLE\s+(\w+)\s+ADD\s+COLUMN\s+(\w+)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AddedColumn();

    [GeneratedRegex(@"UPDATE\s+(\w+)\s+SET\s+(\w+)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BackfilledColumn();
}
