using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The legibility lint (ADR-E27 D10) and the closed content model (ADR-E27 D4). Each finding is an
/// error for an authored or generated contract and a warning for a discovered one, since a
/// discovered contract belongs to its MCP server.
/// </summary>
internal static class ContractLint
{
    /// <summary>Every lint finding for <paramref name="schema"/> and <paramref name="rules"/>, in document order.</summary>
    internal static IReadOnlyList<ContractFinding> Findings(JsonElement schema, IReadOnlyList<ContractRule> rules, ContractProvenance provenance)
    {
        var severity = provenance.LintRefuses ? ContractFindingSeverity.Error : ContractFindingSeverity.Warning;
        var findings = new List<ContractFinding>();
        findings.AddRange(RootFindings(schema, severity));
        foreach (var subschema in SchemaWalk.All(schema))
        {
            findings.AddRange(SubschemaFindings(subschema, severity));
        }

        findings.AddRange(rules.Select((rule, index) => (rule, index))
            .Where(item => string.IsNullOrWhiteSpace(item.rule.Message))
            .Select(item => new ContractFinding(ContractFindingCodes.RuleMessageMissing, severity, $"/rules/{item.index}",
                "Every rule needs a message saying what the payload must satisfy.")));
        return findings;
    }

    /// <summary>The contract-level description and the presence of an example.</summary>
    internal static IEnumerable<ContractFinding> RootFindings(JsonElement schema, ContractFindingSeverity severity)
    {
        if (!HasText(schema, "description"))
        {
            yield return new ContractFinding(ContractFindingCodes.DescriptionMissing, severity, "/schema/description",
                "Add a contract-level description that says what the contract is for, and when not to use it.");
        }

        if (!schema.TryGetProperty("examples", out var examples) || examples.ValueKind != JsonValueKind.Array || examples.GetArrayLength() == 0)
        {
            yield return new ContractFinding(ContractFindingCodes.ExamplesMissing, severity, "/schema/examples",
                "Add at least one example payload under examples; each one must satisfy the contract.");
        }
    }

    /// <summary>A property's description, and an object schema's stated content model.</summary>
    internal static IEnumerable<ContractFinding> SubschemaFindings(Subschema subschema, ContractFindingSeverity severity)
    {
        if (subschema.Position == SubschemaPosition.Property && !HasText(subschema.Schema, "description"))
        {
            yield return new ContractFinding(ContractFindingCodes.PropertyDescriptionMissing, severity, "/schema" + subschema.Pointer,
                "Add a description to this property.");
        }

        if (subschema.Position != SubschemaPosition.Composition && DescribesObject(subschema.Schema) && !StatesContentModel(subschema.Schema))
        {
            yield return new ContractFinding(ContractFindingCodes.ContentModelUnstated, severity, "/schema" + subschema.Pointer,
                "State the content model: \"additionalProperties\": false to close it (the default to aim for), or true to leave it open.");
        }
    }

    /// <summary>Whether <paramref name="schema"/> is an object schema: it says <c>type: object</c> or declares <c>properties</c>.</summary>
    internal static bool DescribesObject(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object
        && (schema.TryGetProperty("properties", out _) || TypeIncludesObject(schema));

    private static bool TypeIncludesObject(JsonElement schema) =>
        schema.TryGetProperty("type", out var type)
        && (type.ValueKind == JsonValueKind.String
            ? type.ValueEquals("object")
            : type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(item => item.ValueEquals("object")));

    private static bool StatesContentModel(JsonElement schema) =>
        schema.TryGetProperty("additionalProperties", out _) || schema.TryGetProperty("unevaluatedProperties", out _);

    private static bool HasText(JsonElement schema, string keyword) =>
        schema.ValueKind == JsonValueKind.Object
        && schema.TryGetProperty(keyword, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString());
}
