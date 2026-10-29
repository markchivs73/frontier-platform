using System.Text.Json;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The checks that refuse a schema whatever its provenance: it must be JSON Schema 2020-12, and every
/// reference must stay inside it (ADR-E27 D3, D9). A reference that leaves the document is how a
/// validator is made to fetch a URL or read a file, so none is allowed.
/// </summary>
internal static class SchemaReferences
{
    /// <summary>The 2020-12 meta-schema URI. <c>$schema</c> may be absent, which means the same.</summary>
    internal const string Dialect2020_12 = "https://json-schema.org/draft/2020-12/schema";

    private static readonly string[] ReferenceKeywords = ["$ref", "$dynamicRef"];

    /// <summary>The dialect and reference findings for <paramref name="schema"/>.</summary>
    internal static IEnumerable<ContractFinding> Findings(JsonElement schema)
    {
        if (schema.TryGetProperty("$schema", out var dialect) && !IsDialect2020_12(dialect))
        {
            yield return new ContractFinding(ContractFindingCodes.DialectUnsupported, ContractFindingSeverity.Error, "/schema/$schema",
                $"Contracts are JSON Schema 2020-12: set $schema to \"{Dialect2020_12}\" or leave it out.");
        }

        foreach (var subschema in SchemaWalk.All(schema).Where(s => s.Schema.ValueKind == JsonValueKind.Object))
        {
            foreach (var keyword in ReferenceKeywords)
            {
                if (subschema.Schema.TryGetProperty(keyword, out var target) && !IsLocal(target))
                {
                    yield return new ContractFinding(ContractFindingCodes.RefNotLocal, ContractFindingSeverity.Error,
                        "/schema" + subschema.Pointer + "/" + SchemaWalk.Escape(keyword),
                        "A reference must point inside this contract (start with \"#\"). Copy the referenced schema under $defs instead.");
                }
            }
        }
    }

    private static bool IsDialect2020_12(JsonElement dialect) =>
        dialect.ValueKind == JsonValueKind.String
        && (dialect.ValueEquals(Dialect2020_12) || dialect.ValueEquals(Dialect2020_12 + "#"));

    private static bool IsLocal(JsonElement target) =>
        target.ValueKind == JsonValueKind.String && target.GetString()!.StartsWith('#');
}
