using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Schema 1.0 → 2.0 migration for the ADR-E3a Decision 3 vocabulary rename (S13.12a): the
/// engine's domain-neutral term for "a named, versioned, gate-approvable node output" is
/// <em>artifact</em>, so stored bytes written before the rename carry <c>section_key</c>
/// and <c>sections</c> where 2.0 carries <c>artifact_key</c> and <c>artifacts</c>.
///
/// Adapters are pure and total (doc 01 §5): they rewrite the stored JSON's key names and
/// deserialize — no value coercion, no defaults invented, original bytes never rewritten
/// (doc 02 §6). This is the one wire break ADR-E3a sanctions, paid once.
/// </summary>
public static class ArtifactVocabularyMigration
{
    /// <summary>The schema version stored bytes carry before the rename.</summary>
    public const string PreRenameSchemaVersion = "1.0";

    /// <summary>The schema version the renamed contracts carry.</summary>
    public const string RenamedSchemaVersion = "2.0";

    private static readonly IReadOnlyDictionary<string, string> RenamedKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["section_key"] = "artifact_key",
        ["sections"] = "artifacts",
        // CascadeCheckNode's own key list — a substring of "section_key", so easy to miss;
        // the golden-diff test below is what catches an omission here.
        ["trigger_section_keys"] = "trigger_artifact_keys",
    };

    /// <summary>Adapter table for <see cref="ContractMigrator.Rehydrate{T}"/> over <see cref="ExecutionSnapshot"/> (its <c>sections</c> map and every nested step's <c>section_key</c>).</summary>
    public static IReadOnlyDictionary<string, Func<JsonElement, JsonSerializerOptions, ExecutionSnapshot>> SnapshotAdapters { get; } =
        new Dictionary<string, Func<JsonElement, JsonSerializerOptions, ExecutionSnapshot>>(StringComparer.Ordinal)
        {
            [PreRenameSchemaVersion] = (element, options) => Migrate<ExecutionSnapshot>(element, options),
        };

    /// <summary>Adapter table for <see cref="ContractMigrator.Rehydrate{T}"/> over <see cref="WorkflowDefinition"/> (each node's <c>section_key</c>).</summary>
    public static IReadOnlyDictionary<string, Func<JsonElement, JsonSerializerOptions, WorkflowDefinition>> DefinitionAdapters { get; } =
        new Dictionary<string, Func<JsonElement, JsonSerializerOptions, WorkflowDefinition>>(StringComparer.Ordinal)
        {
            [PreRenameSchemaVersion] = (element, options) => Migrate<WorkflowDefinition>(element, options),
        };

    /// <summary>Rewrites the pre-rename key names throughout <paramref name="element"/>, stamps the new schema version, and deserializes.</summary>
    internal static T Migrate<T>(JsonElement element, JsonSerializerOptions options)
        where T : IVersionedContract
    {
        var node = JsonNode.Parse(element.GetRawText())!;
        Rename(node);
        node["schema_version"] = RenamedSchemaVersion;

        return node.Deserialize<T>(options)!;
    }

    /// <summary>Depth-first rename of every <see cref="RenamedKeys"/> property, at any nesting depth (a snapshot's steps carry the key too).</summary>
    internal static void Rename(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (from, to) in RenamedKeys)
                {
                    if (obj.Remove(from, out var value))
                    {
                        obj[to] = value;
                    }
                }

                foreach (var property in obj.ToList())
                {
                    Rename(property.Value);
                }

                break;

            case JsonArray array:
                foreach (var item in array.ToList())
                {
                    Rename(item);
                }

                break;
        }
    }
}
