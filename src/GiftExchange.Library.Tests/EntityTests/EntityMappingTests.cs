using System.Text.RegularExpressions;
using GiftExchange.Library.Contexts;
using GiftExchange.Library.Entities;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GiftExchange.Library.Tests.EntityTests;

/// <summary>
/// Liquibase owns the schema and EF owns the mapping, and nothing connects the two. Left alone
/// they drift, and the first symptom is a runtime error against a real cluster. These tests build
/// the model offline and compare it to the migration SQL.
///
/// They also hold the schema to the rules it is written under: no nullable column, no default, and
/// a primary key named for its own table rather than a bare "id".
/// </summary>
public partial class EntityMappingTests
{
    /// <summary>
    /// Builds the model without opening a connection. Any misconfigured relationship or duplicate
    /// mapping throws here.
    /// </summary>
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<GiftExchangeDbContext>()
            .UseNpgsql("Host=localhost;Database=placeholder")
            .Options;

        using var context = new GiftExchangeDbContext(options);
        return context.Model;
    }

    [Fact]
    public void Model_BuildsWithoutError()
    {
        var build = BuildModel;

        build.Should().NotThrow();
    }

    [Fact]
    public void EveryEntity_MapsToATableTheMigrationsCreate()
    {
        var mapped = BuildModel()
            .GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => name is not null)
            .ToImmutableSortedSet()!;

        var surviving = ParseTables().Keys.ToImmutableSortedSet();

        mapped.Should().BeSubsetOf(surviving, "every entity should map to a table the migrations leave in place");
    }

    [Fact]
    public void EveryMappedColumn_ExistsInTheMigrationSql()
    {
        var tables = ParseTables();
        var missing = new List<string>();

        foreach (var entity in BuildModel().GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName is null || !tables.TryGetValue(tableName, out var columns)) continue;

            var storeObject = StoreObjectIdentifier.Table(tableName, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);

                if (columnName is not null && !columns.ContainsKey(columnName))
                    missing.Add($"{entity.ClrType.Name}.{property.Name} maps to {tableName}.{columnName}, which the migrations do not create");
            }
        }

        missing.Should().BeEmpty();
    }

    /// <summary>
    /// The same rule on the other side of the mapping. The SQL and the model are written
    /// independently, so a column that is NOT NULL in one and optional in the other would only
    /// show up against a real cluster — as a write EF was willing to send and DSQL refused.
    /// </summary>
    [Fact]
    public void NoMappedProperty_IsNullable()
    {
        var nullable = BuildModel()
            .GetEntityTypes()
            .SelectMany(
                entity => entity.GetProperties(),
                (entity, property) => new { entity, property })
            .Where(mapped => mapped.property.IsNullable)
            .Select(mapped => $"{mapped.entity.ClrType.Name}.{mapped.property.Name}");

        nullable.Should().BeEmpty();
    }

    /// <summary>
    /// The DDL EF would emit for this model carries no DEFAULT and no nullable column either.
    ///
    /// This reads the generated script rather than the property annotations because
    /// <c>GetDefaultValue()</c> is not the question it looks like: EF answers it with the CLR
    /// default for every property, configured or not. The script is what the model actually means.
    /// </summary>
    [Fact]
    public void TheDdlTheModelWouldEmit_HasNoDefaultsAndNoNullableColumns()
    {
        var options = new DbContextOptionsBuilder<GiftExchangeDbContext>()
            .UseNpgsql("Host=localhost;Database=placeholder")
            .Options;

        using var context = new GiftExchangeDbContext(options);

        var script = context.Database.GenerateCreateScript();

        script.Should().NotContain("DEFAULT", "no column should be given a default");

        var columns = script
            .Split('\n')
            .Select(line => line.Trim().TrimEnd(','))
            .Where(line => ColumnDefinition().IsMatch(line))
            .ToList();

        // Guards the guard: a pattern that matched nothing would let the assertion below pass
        // without having looked at a single column.
        columns.Should().HaveCountGreaterThan(BuildModel().GetEntityTypes().Count());

        columns
            .Where(line => !line.Contains("NOT NULL", StringComparison.Ordinal))
            .Should().BeEmpty("every column the model describes should be NOT NULL");
    }

    /// <summary>
    /// Columns the database is unable to declare NOT NULL, and the application therefore keeps
    /// filled on its own.
    ///
    /// DSQL rejects ALTER COLUMN ... SET NOT NULL, and will not add a column with a default, so a
    /// column introduced after its table already held rows can never be tightened. Listing one here
    /// is a deliberate statement that the application owns the invariant — the same arrangement as
    /// the foreign keys DSQL cannot enforce either — not an oversight.
    ///
    /// Only a column added by a later ALTER can qualify. A column present in a CREATE TABLE has no
    /// excuse, and none belongs here.
    /// </summary>
    private static readonly ImmutableHashSet<string> ColumnsDsqlCannotConstrain =
    [
        // Added by hat--0003. HatEntity.CopiedFromHatId is non-nullable, which is what actually
        // keeps it filled.
        "hat.copied_from_hat_id",

        // Added by hat--0005, backfilled by hat--0006. HatEntity.StatusUpdatedAt is non-nullable,
        // and every write that moves hat.status writes it alongside.
        "hat.status_updated_at",

        // Added by hat--0007 and hat--0008, backfilled by hat--0009. HatEntity.ExchangeDate and
        // HatEntity.ClosePromptSentAt are non-nullable, and every write of a hat states both.
        "hat.exchange_date",
        "hat.close_prompt_sent_at",

        // Added by participant--0003, backfilled by participant--0004. It was written to arrive
        // NOT NULL DEFAULT '', on the theory that a default is what lets a column be added
        // constrained to a table already holding rows -- DSQL took neither half, which is where the
        // second clause of the summary above comes from. ParticipantEntity.Emoji is non-nullable
        // and a face is chosen when a participant is added, so every row written since carries one.
        "participant.emoji",

        // Added by gift_idea--0002, backfilled by gift_idea--0003. GiftIdeaEntity.HoldUntilAsked is
        // non-nullable, and the one path that writes a submission states it either way -- the share
        // form posts the checkbox or it does not.
        "gift_idea.hold_until_asked",

        // Added by person--0003, backfilled by person--0004. PersonEntity.AddedByPersonId is
        // non-nullable, and the two paths that write a person -- ResolvePersonIdAsync and the
        // address correction that lands on an address nobody holds -- both state it.
        "person.added_by_person_id"
    ];

    /// <summary>
    /// Nothing is nullable. Absence is spelled with a value instead — the all-zero UUID, the
    /// minimum timestamp, the empty string — so reading a row never means asking whether a column
    /// is there.
    /// </summary>
    [Fact]
    public void NoSurvivingColumn_IsNullable()
    {
        var nullable = ParseTables()
            .SelectMany(
                table => table.Value,
                (table, column) => new { table, column })
            .Where(declared =>
                !declared.column.Value.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase)
                && !declared.column.Value.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
            .Select(declared => $"{declared.table.Key}.{declared.column.Key}");

        nullable.Should().BeSubsetOf(ColumnsDsqlCannotConstrain);
    }

    /// <summary>
    /// An entry that names a column the migrations do declare NOT NULL is stale, and would quietly
    /// excuse the next column to take its place.
    /// </summary>
    [Fact]
    public void EveryDocumentedNullableColumn_IsStillNullable()
    {
        var nullable = ParseTables()
            .SelectMany(
                table => table.Value,
                (table, column) => new { table, column })
            .Where(declared =>
                !declared.column.Value.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase)
                && !declared.column.Value.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
            .Select(declared => $"{declared.table.Key}.{declared.column.Key}");

        ColumnsDsqlCannotConstrain.Should().BeSubsetOf(nullable);
    }

    /// <summary>
    /// Nothing has a default. Every field is stated by whoever writes the row, so what the
    /// application believes it wrote is what is there.
    /// </summary>
    [Fact]
    public void NoSurvivingColumn_HasADefault()
    {
        var defaulted = ParseTables()
            .SelectMany(
                table => table.Value,
                (table, column) => new { table, column })
            .Where(declared => declared.column.Value.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase))
            .Select(declared => $"{declared.table.Key}.{declared.column.Key}");

        defaulted.Should().BeEmpty();
    }

    /// <summary>
    /// A primary key is named for the table it identifies, never a bare "id", so a column keeps
    /// its meaning once it has been carried into another table as a foreign key.
    /// </summary>
    [Fact]
    public void EveryPrimaryKey_IsNamedForItsTable()
    {
        var misnamed = ParseTables()
            .SelectMany(
                table => table.Value,
                (table, column) => new { table, column })
            .Where(declared =>
                declared.column.Value.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase)
                && declared.column.Key != $"{declared.table.Key}_id")
            .Select(declared =>
                $"{declared.table.Key}.{declared.column.Key} should be {declared.table.Key}_id");

        misnamed.Should().BeEmpty();
    }

    /// <summary>
    /// Values the application writes into a column verbatim, and the property each set belongs to.
    ///
    /// These are closed vocabularies — a status or a message type is one of a handful of constants
    /// this codebase spells out — so unlike free text there is no length to guess at and no
    /// truncation that would make sense. Either every member fits its column or one of them does
    /// not, and the one that does not fails every insert it ever appears in.
    /// </summary>
    private static readonly ImmutableList<(string Entity, string Property, ImmutableList<string> Values)> Vocabularies =
    [
        // Unspecified is deliberately absent from EmailMessageTypes.All — it is what an untagged
        // message becomes, never something a send may tag itself with — but it reaches the column
        // like the rest, so it is held to the same width.
        (
            nameof(ParticipantEmailDeliveryEntity),
            nameof(ParticipantEmailDeliveryEntity.MessageType),
            EmailMessageTypes.All.Add(EmailMessageType.Unspecified)
        ),
        (
            nameof(ParticipantEmailDeliveryEntity),
            nameof(ParticipantEmailDeliveryEntity.Status),
            DeliveryStatuses.All
        ),
        (nameof(HatEntity), nameof(HatEntity.Status), HatStatuses.All),

        // Not a vocabulary in the same sense -- a face means nothing to the application, and this
        // column is never compared against a constant. It is here for the one thing the others are
        // checked for: every value the application can write fits the column it is written into,
        // which for an emoji is not obvious by looking.
        (nameof(ParticipantEntity), nameof(ParticipantEntity.Emoji), PersonEmoji.All)
    ];

    /// <summary>
    /// No constant is longer than the column it is written into.
    ///
    /// ORGANIZER_PARTICIPANT_LEFT was twenty-six characters against a twenty character column, and
    /// nothing between the constant and the cluster had an opinion about it: EF does not enforce
    /// its own HasMaxLength on save, so the first thing to notice was DSQL rejecting the insert —
    /// once for every organizer-left notice sent, retried six minutes apart until the queue gave
    /// up on it.
    ///
    /// The limit is read from the model rather than restated here, so this cannot pass by agreeing
    /// with a number that has since moved.
    /// </summary>
    [Fact]
    public void EveryConstantWrittenToAColumn_FitsIt()
    {
        var model = BuildModel();
        var offences = new List<string>();

        foreach (var (entityName, propertyName, values) in Vocabularies)
        {
            var property = model
                .GetEntityTypes()
                .Single(entity => entity.ClrType.Name == entityName)
                .GetProperty(propertyName);

            var maxLength = property.GetMaxLength();

            // Guards the guard: an unconstrained property would excuse every value written to it.
            maxLength.Should().NotBeNull($"{entityName}.{propertyName} should declare a maximum length");

            // And so would an empty vocabulary.
            values.Should().NotBeEmpty($"{entityName}.{propertyName} should have values to check");

            offences.AddRange(values
                .Where(value => value.Length > maxLength)
                .Select(value =>
                    $"{entityName}.{propertyName} holds {maxLength} characters and \"{value}\" is {value.Length}"));
        }

        offences.Should().BeEmpty();
    }

    /// <summary>
    /// A mapped length says the same thing as the column it describes.
    ///
    /// This file's premise bites hardest here. DSQL has no ALTER COLUMN, so a VARCHAR is the width
    /// its CREATE TABLE gave it for as long as the table exists, and a HasMaxLength that disagrees
    /// is a mistake no migration can undo: claiming the smaller number refuses writes the database
    /// would have taken, and claiming the larger one sends writes the database will refuse.
    /// </summary>
    [Fact]
    public void EveryMappedLength_MatchesTheColumnItDescribes()
    {
        var tables = ParseTables();
        var mismatched = new List<string>();
        var compared = 0;

        foreach (var entity in BuildModel().GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName is null || !tables.TryGetValue(tableName, out var columns)) continue;

            var storeObject = StoreObjectIdentifier.Table(tableName, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                var maxLength = property.GetMaxLength();
                var columnName = property.GetColumnName(storeObject);

                if (maxLength is null || columnName is null || !columns.TryGetValue(columnName, out var definition))
                    continue;

                var declared = VarcharLength().Match(definition);

                if (!declared.Success)
                {
                    mismatched.Add(
                        $"{entity.ClrType.Name}.{property.Name} is mapped with a maximum length, but "
                        + $"{tableName}.{columnName} is not a VARCHAR");
                    continue;
                }

                compared++;

                if (int.Parse(declared.Groups[1].Value) != maxLength)
                    mismatched.Add(
                        $"{entity.ClrType.Name}.{property.Name} is mapped as {maxLength} and "
                        + $"{tableName}.{columnName} is {declared.Groups[1].Value}");
            }
        }

        // Guards the guard: a parse that matched nothing would agree with everything.
        compared.Should().BeGreaterThan(0, "some columns should have been compared");

        mismatched.Should().BeEmpty();
    }

    [GeneratedRegex(@"VARCHAR\s*\(\s*(\d+)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex VarcharLength();

    /// <summary>
    /// Replays the migrations far enough to know which tables survive and which columns each of
    /// them ends up with, keyed by column name and holding the full definition so the rules above
    /// have something to read.
    ///
    /// Applied in changelog order, which is the order Liquibase applies them, because a column can
    /// be introduced by a later ALTER rather than the original CREATE and a table can be dropped
    /// entirely further down.
    /// </summary>
    private static Dictionary<string, ImmutableDictionary<string, string>> ParseTables()
    {
        var tables = new Dictionary<string, ImmutableDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, sql) in Migrations.Statements)
        {
            var create = Regex.Match(sql, @"CREATE\s+TABLE\s+(\w+)\s*\((.*)\)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (create.Success)
            {
                tables[create.Groups[1].Value] = ParseColumns(create.Groups[2].Value);
                continue;
            }

            var dropTable = Regex.Match(sql, @"DROP\s+TABLE\s+(\w+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (dropTable.Success)
            {
                tables.Remove(dropTable.Groups[1].Value);
                continue;
            }

            var addColumn = Regex.Match(sql, @"ALTER\s+TABLE\s+(\w+)\s+ADD\s+COLUMN\s+(\w+)([^;]*)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (addColumn.Success && tables.TryGetValue(addColumn.Groups[1].Value, out var existing))
            {
                tables[addColumn.Groups[1].Value] = existing.SetItem(
                    addColumn.Groups[2].Value,
                    addColumn.Groups[3].Value.Trim());
                continue;
            }

            var dropColumn = Regex.Match(sql, @"ALTER\s+TABLE\s+(\w+)\s+DROP\s+COLUMN\s+(\w+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (dropColumn.Success && tables.TryGetValue(dropColumn.Groups[1].Value, out var remaining))
                tables[dropColumn.Groups[1].Value] = remaining.Remove(dropColumn.Groups[2].Value);
        }

        return tables;
    }

    private static ImmutableDictionary<string, string> ParseColumns(string tableBody) =>
        tableBody
            .Split(',')
            .Select(definition => definition.Trim())
            .Where(definition => definition.Length > 0)
            // Table-level constraints rather than columns.
            .Where(definition => !IsConstraintKeyword(FirstToken(definition)))
            .ToImmutableDictionary(FirstToken, definition => definition, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A column line in a generated create script: a lower-case identifier, then its type.
    /// CONSTRAINT and CREATE lines start with a capital, so they fall outside it.
    /// </summary>
    [GeneratedRegex(@"^[a-z_]+ \w")]
    private static partial Regex ColumnDefinition();

    private static string FirstToken(string definition) =>
        definition.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)[0];

    private static bool IsConstraintKeyword(string token) =>
        token.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)
        || token.Equals("UNIQUE", StringComparison.OrdinalIgnoreCase)
        || token.Equals("FOREIGN", StringComparison.OrdinalIgnoreCase)
        || token.Equals("CHECK", StringComparison.OrdinalIgnoreCase)
        || token.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase);
}
