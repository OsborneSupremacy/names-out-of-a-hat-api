using System.Reflection;
using System.Text.Json;

namespace GiftExchange.Library.Tests.SchemaTests;

/// <summary>
/// The JSON schemas are hand-maintained but describe types the serializer produces, so they drift
/// quietly. API Gateway does not enforce response models at all, and it only enforces request
/// models against what a client sends — never against the records on this side. Nothing else
/// compares the two, so this does.
/// </summary>
public class SchemaDriftTests
{
    private static readonly string SchemaDirectory = Path.Combine(AppContext.BaseDirectory, "Schemas");

    /// <summary>
    /// Schema files that describe nothing this codebase serializes. Listing one here is a
    /// deliberate statement that it is unused, not an oversight.
    /// </summary>
    private static readonly ImmutableHashSet<string> UnusedSchemas =
    [
        // Nothing references this, and DeleteHat returns 204 with no body at all.
        "DeleteHatResponse.schema.json"
    ];

    /// <summary>
    /// type name, schema file, and the path to the described object within that schema
    /// ("" for the root).
    /// </summary>
    public static TheoryData<string, string, string> Subjects => new()
    {
        { nameof(AddParticipantRequest), "AddParticipantRequest.schema.json", "" },
        { nameof(AssignRecipientsRequest), "AssignRecipientsRequest.schema.json", "" },
        { nameof(CloseHatRequest), "CloseHatRequest.schema.json", "" },
        { nameof(CopyHatRequest), "CopyHatRequest.schema.json", "" },
        { nameof(CopyHatResponse), "CopyHatResponse.schema.json", "" },
        { nameof(CreateHatRequest), "CreateHatRequest.schema.json", "" },
        { nameof(CreateHatResponse), "CreateHatResponse.schema.json", "" },
        { nameof(DeleteHatRequest), "DeleteHatRequest.schema.json", "" },
        { nameof(DeleteMyDataRequest), "DeleteMyDataRequest.schema.json", "" },
        { nameof(EditHatRequest), "EditHatRequest.schema.json", "" },
        { nameof(EditParticipantRequest), "EditParticipantRequest.schema.json", "" },
        { nameof(EditParticipantAddressRequest), "EditParticipantAddressRequest.schema.json", "" },
        { nameof(EditParticipantAddressResponse), "EditParticipantAddressResponse.schema.json", "" },
        { nameof(EditParticipantEmojiRequest), "EditParticipantEmojiRequest.schema.json", "" },
        { nameof(EditParticipantNameRequest), "EditParticipantNameRequest.schema.json", "" },
        { nameof(ErrorResponse), "ErrorResponse.schema.json", "" },
        { nameof(ResetHatRequest), "ResetHatRequest.schema.json", "" },

        { nameof(ExportHatResponse), "ExportHatResponse.schema.json", "" },
        { nameof(ExportedHat), "ExportHatResponse.schema.json", "definitions/exportedhat" },
        { nameof(ExportedPerson), "ExportHatResponse.schema.json", "definitions/exportedperson" },
        { nameof(ExportedParticipant), "ExportHatResponse.schema.json", "definitions/exportedparticipant" },
        { nameof(ExportedParticipantReference), "ExportHatResponse.schema.json", "definitions/exportedparticipantreference" },
        { nameof(PreviewInvitationsRequest), "PreviewInvitationsRequest.schema.json", "" },
        { nameof(PreviewInvitationsResponse), "PreviewInvitationsResponse.schema.json", "" },
        { nameof(RemoveParticipantRequest), "RemoveParticipantRequest.schema.json", "" },
        { nameof(SendInvitationsRequest), "SendInvitationsRequest.schema.json", "" },
        { nameof(ValidateHatRequest), "ValidateHatRequest.schema.json", "" },
        { nameof(SubmitFeedbackRequest), "SubmitFeedbackRequest.schema.json", "" },
        { nameof(UpdateProfileRequest), "UpdateProfileRequest.schema.json", "" },
        { nameof(ValidateHatResponse), "ValidateHatResponse.schema.json", "" },

        { nameof(Hat), "Hat.schema.json", "" },
        { nameof(Person), "Hat.schema.json", "definitions/person" },
        { nameof(Participant), "Hat.schema.json", "definitions/participant" },

        { nameof(Participant), "Participant.schema.json", "" },
        { nameof(Person), "Participant.schema.json", "definitions/person" },

        { nameof(GetHatsResponse), "GetHatsResponse.schema.json", "" },
        { nameof(HatMetaData), "GetHatsResponse.schema.json", "definitions/hatmetadata" }
    };

    [Theory]
    [MemberData(nameof(Subjects))]
    public void Schema_DescribesExactlyWhatTheRecordSerializes(
        string typeName,
        string schemaFile,
        string definitionPath
    )
    {
        var serialized = GetSerializedPropertyNames(ResolveType(typeName));
        var documented = GetSchemaPropertyNames(schemaFile, definitionPath);

        documented.Should().BeEquivalentTo(
            serialized,
            $"{schemaFile} should describe every property {typeName} serializes, and no others"
        );
    }

    [Fact]
    public void EverySchemaFile_IsEitherCoveredOrDeclaredUnused()
    {
        var covered = Subjects
            .Select(subject => (string)subject[1])
            .Concat(UnusedSchemas)
            .ToImmutableHashSet();

        var onDisk = Directory
            .GetFiles(SchemaDirectory, "*.schema.json")
            .Select(file => Path.GetFileName(file)!)
            .ToImmutableHashSet();

        onDisk.Except(covered).Should().BeEmpty("every schema should be checked against a record, or listed as unused");
        covered.Except(onDisk).Should().BeEmpty("the coverage list should not name schema files that no longer exist");
    }

    /// <summary>
    /// API Gateway validates models against JSON Schema draft-04. Keywords from later drafts are
    /// rejected at deploy time with "Unsupported keyword(s)", and a $ref that is not a plain
    /// #/definitions/... pointer fails its canonical form check. Both are slow ways to find out.
    /// </summary>
    private static readonly ImmutableHashSet<string> KeywordsApiGatewayRejects =
    [
        "$defs", "$anchor", "$dynamicRef", "$dynamicAnchor", "const", "if", "then", "else",
        "contains", "prefixItems", "unevaluatedProperties", "unevaluatedItems",
        "dependentSchemas", "dependentRequired"
    ];

    [Fact]
    public void EverySchemaFile_UsesOnlyWhatApiGatewayAccepts()
    {
        var offences = new List<string>();

        foreach (var file in Directory.GetFiles(SchemaDirectory, "*.schema.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            Inspect(document.RootElement, document.RootElement, Path.GetFileName(file)!, offences);
        }

        offences.Should().BeEmpty();
    }

    private static void Inspect(JsonElement node, JsonElement root, string fileName, List<string> offences)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    if (KeywordsApiGatewayRejects.Contains(property.Name))
                        offences.Add($"{fileName}: uses '{property.Name}', which API Gateway rejects");

                    if (property.NameEquals("$ref"))
                        InspectReference(property.Value.GetString(), root, fileName, offences);
                    else
                        Inspect(property.Value, root, fileName, offences);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                    Inspect(item, root, fileName, offences);
                break;
        }
    }

    private static void InspectReference(string? reference, JsonElement root, string fileName, List<string> offences)
    {
        if (reference is null || !reference.StartsWith("#/definitions/", StringComparison.Ordinal))
        {
            offences.Add($"{fileName}: $ref '{reference}' is not the canonical #/definitions/... form");
            return;
        }

        var node = root;

        foreach (var segment in reference["#/".Length..].Split('/'))
        {
            if (!node.TryGetProperty(segment, out var child))
            {
                offences.Add($"{fileName}: $ref '{reference}' does not resolve");
                return;
            }

            node = child;
        }
    }

    /// <summary>
    /// The categories the contact form offers live in three places: this schema's enum, which is
    /// what API Gateway rejects a bad request against; <see cref="FeedbackCategories.All"/>, which
    /// is what the validator accepts; and the frontend's list. The first two are checked here.
    /// Drift between them means a category the form offers is refused at the edge, or one the
    /// gateway waves through that the validator then rejects with a different message.
    /// </summary>
    [Fact]
    public void FeedbackSchema_OffersExactlyTheCategoriesTheValidatorAccepts()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SchemaDirectory, "SubmitFeedbackRequest.schema.json")));

        var documented = document.RootElement
            .GetProperty("properties")
            .GetProperty("category")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(entry => entry.GetString()!);

        documented.Should().BeEquivalentTo(FeedbackCategories.All);
    }

    /// <summary>
    /// A name in "required" that the same subschema never declares as a property is invisible to
    /// the property comparison above, which reads "properties" and nothing else. Combined with
    /// "additionalProperties": false it is worse than cosmetic: the subschema forbids the very
    /// name it demands, so nothing can satisfy it. API Gateway does not enforce response models,
    /// so such a schema fails silently there and only bites whoever generates a client from it.
    /// </summary>
    [Fact]
    public void EverySchemaFile_RequiresOnlyPropertiesItDeclares()
    {
        var offences = new List<string>();

        foreach (var file in Directory.GetFiles(SchemaDirectory, "*.schema.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            InspectRequired(document.RootElement, "(root)", Path.GetFileName(file)!, offences);
        }

        offences.Should().BeEmpty();
    }

    private static void InspectRequired(JsonElement schema, string location, string fileName, List<string> offences)
    {
        if (schema.ValueKind is not JsonValueKind.Object)
            return;

        var hasProperties = schema.TryGetProperty("properties", out var properties)
                            && properties.ValueKind is JsonValueKind.Object;

        if (schema.TryGetProperty("required", out var required) && required.ValueKind is JsonValueKind.Array)
        {
            var declared = hasProperties
                ? properties.EnumerateObject().Select(property => property.Name).ToImmutableHashSet()
                : ImmutableHashSet<string>.Empty;

            foreach (var name in required.EnumerateArray()
                         .Where(entry => entry.ValueKind is JsonValueKind.String)
                         .Select(entry => entry.GetString()!)
                         .Where(name => !declared.Contains(name)))
            {
                offences.Add($"{fileName}: {location} requires '{name}', which it does not declare as a property");
            }
        }

        // Descend only through keywords whose values are themselves schemas. Walking blindly would
        // read a property literally named "required" or "properties" as a keyword of its parent.
        if (hasProperties)
        {
            foreach (var property in properties.EnumerateObject())
                InspectRequired(property.Value, $"{location}/{property.Name}", fileName, offences);
        }

        if (schema.TryGetProperty("definitions", out var definitions) && definitions.ValueKind is JsonValueKind.Object)
        {
            foreach (var definition in definitions.EnumerateObject())
                InspectRequired(definition.Value, $"definitions/{definition.Name}", fileName, offences);
        }

        if (schema.TryGetProperty("items", out var items))
            InspectRequired(items, $"{location}/items", fileName, offences);
    }

    [Fact]
    public void EverySchemaFile_IsValidJson()
    {
        foreach (var file in Directory.GetFiles(SchemaDirectory, "*.schema.json"))
        {
            var parse = () => JsonDocument.Parse(File.ReadAllText(file));
            parse.Should().NotThrow($"{Path.GetFileName(file)} should be valid JSON");
        }
    }

    private static Type ResolveType(string typeName) =>
        typeof(Hat).Assembly
            .GetTypes()
            .Single(type => type.Name == typeName);

    /// <summary>
    /// Mirrors what the serializer emits: public instance properties, camel cased by the same
    /// policy configured in <see cref="Builders.ServiceProviderBuilder"/>.
    /// </summary>
    private static ImmutableSortedSet<string> GetSerializedPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .ToImmutableSortedSet();

    private static ImmutableSortedSet<string> GetSchemaPropertyNames(string schemaFile, string definitionPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaDirectory, schemaFile)));

        var node = document.RootElement;

        foreach (var segment in definitionPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            node = node.GetProperty(segment);

        return node.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToImmutableSortedSet();
    }
}
