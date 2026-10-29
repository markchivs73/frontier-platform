using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Contracts;

/// <summary>
/// Compatibility within a major (ADR-E27 D5; Mark, 2026-10-29: strict and sound). Against every
/// earlier minor of the same major, a new minor may change annotations only, remove rules, or edit a
/// rule's message or field path. Any structural schema change, and any added or changed rule
/// expression, needs a new major. With closed content models this is what FULL_TRANSITIVE means:
/// each minor accepts exactly the data every other minor accepts, apart from the rules it dropped.
/// </summary>
internal static class ContractCompatibility
{
    /// <summary>
    /// The keywords that never change what a schema accepts. <c>format</c> and the
    /// <c>content*</c> keywords are annotations here because the validator never asserts them.
    /// </summary>
    internal static readonly IReadOnlySet<string> AnnotationKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "title", "description", "examples", "$comment", "deprecated", "readOnly", "writeOnly", "default",
        "format", "contentEncoding", "contentMediaType", "contentSchema",
    };

    /// <summary>The version and compatibility findings for <paramref name="candidate"/> against <paramref name="registered"/>.</summary>
    internal static IReadOnlyList<ContractFinding> Findings(
        ContractRef candidate, JsonElement schema, IReadOnlyList<ContractRule> rules, IReadOnlyList<ContractSnapshot> registered)
    {
        var sameMajor = SameMajor(candidate, registered);
        var findings = new List<ContractFinding>();
        findings.AddRange(VersionFindings(candidate, sameMajor.Select(item => item.Ref)));
        var structure = Structure(schema);
        foreach (var (earlierRef, earlier) in sameMajor.Where(item => item.Ref.Minor < candidate.Minor))
        {
            findings.AddRange(EarlierFindings(earlierRef, earlier, structure, rules));
        }

        return findings;
    }

    /// <summary>The registered versions with the candidate's id and major, oldest first.</summary>
    internal static IReadOnlyList<(ContractRef Ref, ContractSnapshot Snapshot)> SameMajor(ContractRef candidate, IReadOnlyList<ContractSnapshot> registered) =>
        registered
            .Select(snapshot => (Parsed: ContractRef.TryParse(snapshot.ContractRef, out var parsed) ? parsed : null, Snapshot: snapshot))
            .Where(item => item.Parsed is not null && item.Parsed.Id == candidate.Id && item.Parsed.Major == candidate.Major)
            .Select(item => (item.Parsed!, item.Snapshot))
            .OrderBy(item => item.Item1.Minor)
            .ToList();

    /// <summary>A registered version never changes, and a new minor must come after every registered one.</summary>
    internal static IEnumerable<ContractFinding> VersionFindings(ContractRef candidate, IEnumerable<ContractRef> sameMajor)
    {
        var minors = sameMajor.Select(item => item.Minor).ToList();
        if (minors.Contains(candidate.Minor))
        {
            yield return new ContractFinding(ContractFindingCodes.VersionExists, ContractFindingSeverity.Error, "/contract_ref",
                $"{candidate} is already registered, and a registered contract never changes. Register {candidate.Id}/{candidate.Major}.{minors.Max() + 1} instead.");
        }
        else if (minors.Count > 0 && minors.Max() > candidate.Minor)
        {
            yield return new ContractFinding(ContractFindingCodes.VersionNotNewer, ContractFindingSeverity.Error, "/contract_ref",
                $"{candidate.Id}/{candidate.Major}.{minors.Max()} is already registered; a new minor must come after it.");
        }
    }

    /// <summary>The findings against one earlier minor.</summary>
    internal static IEnumerable<ContractFinding> EarlierFindings(ContractRef earlierRef, ContractSnapshot earlier, byte[] structure, IReadOnlyList<ContractRule> rules)
    {
        if (!structure.AsSpan().SequenceEqual(Structure(earlier.Schema)))
        {
            yield return new ContractFinding(ContractFindingCodes.MinorChangesSchema, ContractFindingSeverity.Error, "/schema",
                $"The schema accepts different data from {earlierRef}. Within a major a minor may change only descriptions, titles, examples and other annotations; this change needs a new major.");
        }

        var earlierRules = earlier.Rules.Select(rule => rule.Rule).ToHashSet(StringComparer.Ordinal);
        foreach (var (rule, index) in rules.Select((rule, index) => (rule, index)).Where(item => !earlierRules.Contains(item.rule.Rule)))
        {
            yield return new ContractFinding(ContractFindingCodes.MinorChangesRules, ContractFindingSeverity.Error, $"/rules/{index}",
                $"This rule is not in {earlierRef}. Adding or changing a rule narrows what readers accept, so it needs a new major.");
        }
    }

    /// <summary>The JCS bytes of <paramref name="schema"/> with every annotation removed.</summary>
    internal static byte[] Structure(JsonElement schema)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteSchema(writer, schema);
        }

        return JsonCanonicalizer.Canonicalize(stream.ToArray());
    }

    /// <summary>Writes a schema without its annotations, descending only through applicator keywords.</summary>
    internal static void WriteSchema(Utf8JsonWriter writer, JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            schema.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        foreach (var keyword in schema.EnumerateObject().Where(keyword => !AnnotationKeywords.Contains(keyword.Name)))
        {
            writer.WritePropertyName(keyword.Name);
            WriteKeyword(writer, keyword);
        }

        writer.WriteEndObject();
    }

    private static void WriteKeyword(Utf8JsonWriter writer, JsonProperty keyword)
    {
        if (SchemaWalk.SingleKeywords.Contains(keyword.Name))
        {
            WriteSchema(writer, keyword.Value);
        }
        else if (SchemaWalk.MapKeywords.Contains(keyword.Name) && keyword.Value.ValueKind == JsonValueKind.Object)
        {
            WriteSchemaMap(writer, keyword.Value);
        }
        else if (SchemaWalk.ArrayKeywords.Contains(keyword.Name) && keyword.Value.ValueKind == JsonValueKind.Array)
        {
            WriteSchemaArray(writer, keyword.Value);
        }
        else
        {
            keyword.Value.WriteTo(writer);
        }
    }

    private static void WriteSchemaMap(Utf8JsonWriter writer, JsonElement map)
    {
        writer.WriteStartObject();
        foreach (var entry in map.EnumerateObject())
        {
            writer.WritePropertyName(entry.Name);
            WriteSchema(writer, entry.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteSchemaArray(Utf8JsonWriter writer, JsonElement array)
    {
        writer.WriteStartArray();
        foreach (var item in array.EnumerateArray())
        {
            WriteSchema(writer, item);
        }

        writer.WriteEndArray();
    }
}
