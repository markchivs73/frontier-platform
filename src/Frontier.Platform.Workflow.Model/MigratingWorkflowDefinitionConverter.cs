using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Reads a stored <see cref="WorkflowDefinition"/> forward from an older schema major.
///
/// <para>Stored definitions have never migrated. Snapshots rehydrate through
/// <c>ContractMigrator</c>, but definitions deserialize straight through the store's serializer,
/// so bytes written before the artifact-vocabulary rename come back with every node's
/// <c>artifact_key</c> silently <see langword="null"/> — the old <c>section_key</c> is simply an
/// unrecognised property and is dropped. The definition then fails validation for reasons that
/// read as content problems (a gate's rollback target "produces no artifact"), and no amount of
/// editing fixes it: the designer's merge is validation-gated, so a repair that would make the
/// draft valid can never be written. The draft is unrecoverable through the product.</para>
///
/// <para>This closes that. It is a property-level converter rather than a change to the shared
/// canonical profile, deliberately: the profile is governance-tier and must not learn about
/// engine types (ADR-PA5), and one converter covers every path a stored definition arrives
/// through — drafts, versions, persisted proposals, and the orchestration input replayed from
/// durable history — without restructuring any of them.</para>
///
/// <para><b>It lives in the model rather than the compiler because history is the other reader.</b>
/// A definition rides inline in the orchestration input (ADR-2), so it is rehydrated from
/// recorded history on every replay. Migration there is deterministic by construction: the
/// recorded bytes never change and the adapter is a pure total function of them, so every replay
/// yields the identical definition. It does not weaken replay — it is what keeps replay working
/// across a schema change, since the alternative is a definition that rehydrates with null
/// artifact keys and makes different scheduling decisions than the run being replayed.</para>
///
/// <para><b>Writes are untouched.</b> Serialization always emits the current schema, so a
/// migrated document is written back in current form the next time it is saved, and the
/// definition hash of anything already published is unaffected because nothing is rewritten
/// behind the caller's back.</para>
/// </summary>
public sealed class MigratingWorkflowDefinitionConverter : JsonConverter<WorkflowDefinition>
{
    /// <inheritdoc />
    public override WorkflowDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;

        var storedVersion = element.TryGetProperty("schema_version", out var v) ? v.GetString() : null;

        if (storedVersion is not null
            && ArtifactVocabularyMigration.DefinitionAdapters.TryGetValue(storedVersion, out var adapter))
        {
            return adapter(element, options);
        }

        // Current (or newer-minor) schema: deserialize normally. This does not re-enter the
        // converter — it is applied per property, not registered on the options.
        return element.Deserialize<WorkflowDefinition>(options)
            ?? throw new JsonException("A stored workflow definition deserialized to null.");
    }

    /// <summary>
    /// Reads a definition from raw JSON with the same forward migration, for the paths that
    /// deserialize a definition by hand rather than through a stored document — the agent's
    /// persisted proposal being the one that can then be merged and written.
    /// </summary>
    public static WorkflowDefinition? ReadMigrated(string json, JsonSerializerOptions options)
    {
        using var document = JsonDocument.Parse(json);
        var element = document.RootElement;

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var storedVersion = element.TryGetProperty("schema_version", out var v) ? v.GetString() : null;

        return storedVersion is not null
            && ArtifactVocabularyMigration.DefinitionAdapters.TryGetValue(storedVersion, out var adapter)
                ? adapter(element, options)
                : element.Deserialize<WorkflowDefinition>(options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, WorkflowDefinition value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
