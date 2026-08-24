using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Model.Tests.Serialization;

/// <summary>
/// S13.12a (ADR-E3a D3): the 1.0 → 2.0 artifact-rename migration, exercised against the
/// <em>real</em> pre-rename golden bytes preserved under <c>GoldenFiles/legacy-v1/</c> —
/// not hand-written fixtures, so the adapter is proven against exactly what the platform
/// used to write. Old goldens stay (append-only); stored bytes are never rewritten.
/// </summary>
public sealed class ArtifactVocabularyMigrationTests
{
    [Theory]
    [InlineData("execution_snapshot.v1.json")]
    [InlineData("execution_snapshot_host_build.v1.json")]
    [InlineData("execution_snapshot_initiated.v1.json")]
    [InlineData("execution_snapshot_paused_on_failure.v1.json")]
    [InlineData("execution_snapshot_skipped.v1.json")]
    public void PreRenameSnapshot_RehydratesWithArtifactVocabulary(string legacyGolden)
    {
        var bytes = LegacyBytes(legacyGolden);

        var snapshot = ContractMigrator.Rehydrate(bytes, CanonicalProfile.Options, ArtifactVocabularyMigration.SnapshotAdapters);

        // Compared against the type's current version, not against
        // ArtifactVocabularyMigration.RenamedSchemaVersion — which is the adapter's own constant,
        // so asserting on it compares the adapter to itself and keeps passing after the next bump
        // leaves this adapter behind. MigrationReachesCurrentSchemaTests owns that guarantee; this
        // line just stops being a booby trap.
        Assert.Equal(
            MigrationReachesCurrentSchemaTests.CurrentSchemaVersionOf<ExecutionSnapshot>("execution_snapshot.json"),
            snapshot.SchemaVersion);

        // The map survived the key rename with its contents intact — compared against the
        // legacy file's own `sections` object rather than a hardcoded expectation, so this
        // holds for the minimal fixtures (empty map) and the populated ones alike.
        using var legacy = JsonDocument.Parse(bytes);
        var expected = legacy.RootElement.GetProperty("sections").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        Assert.Equal(expected, snapshot.Artifacts.ToDictionary(p => p.Key, p => p.Value.Name, StringComparer.Ordinal));
        Assert.All(snapshot.Artifacts.Values, status => Assert.Contains(status, ArtifactStatus.List));
        snapshot.Validate();
    }

    [Fact]
    public void PreRenameSnapshot_PreservesNestedStepArtifactKeys()
    {
        // section_key is nested inside completed_steps[] — a root-only rename would silently
        // drop it (the failure mode this test exists to catch).
        var legacy = JsonDocument.Parse(LegacyBytes("execution_snapshot.v1.json"));
        var expected = legacy.RootElement.GetProperty("completed_steps")[0].GetProperty("section_key").GetString();

        var snapshot = ContractMigrator.Rehydrate(LegacyBytes("execution_snapshot.v1.json"), CanonicalProfile.Options, ArtifactVocabularyMigration.SnapshotAdapters);

        Assert.Equal(expected, snapshot.CompletedSteps[0].ArtifactKey);
        Assert.NotNull(snapshot.CompletedSteps[0].ArtifactKey);
    }

    [Fact]
    public void PreRenameDefinition_RehydratesNodeArtifactKeys()
    {
        var legacy = JsonDocument.Parse(LegacyBytes("workflow_definition.v1.json"));
        var expected = legacy.RootElement.GetProperty("nodes")
            .EnumerateArray()
            .Select(n => n.TryGetProperty("section_key", out var k) ? k.GetString() : null)
            .ToList();

        var definition = ContractMigrator.Rehydrate(LegacyBytes("workflow_definition.v1.json"), CanonicalProfile.Options, ArtifactVocabularyMigration.DefinitionAdapters);

        // The type's current version, not the adapter's own constant — see the note above.
        Assert.Equal(
            MigrationReachesCurrentSchemaTests.CurrentSchemaVersionOf<WorkflowDefinition>("workflow_definition.json"),
            definition.SchemaVersion);
        Assert.Equal(expected, definition.Nodes.Select(n => n.ArtifactKey));
        Assert.Contains(definition.Nodes, n => n.ArtifactKey is not null); // the fixture really does carry keys
    }

    [Fact]
    public void PostRenameBytes_NeedNoAdapter()
    {
        // 2.0 bytes deserialize directly — the adapter table is keyed on 1.0 only, so a
        // current-shape document takes the plain path (doc 01 §5's minor-add-with-defaults rule).
        var current = File.ReadAllBytes(GoldenPath("execution_snapshot.json"));

        var snapshot = ContractMigrator.Rehydrate(current, CanonicalProfile.Options, ArtifactVocabularyMigration.SnapshotAdapters);

        Assert.Equal("2.0", snapshot.SchemaVersion);
        Assert.NotEmpty(snapshot.Artifacts);
    }

    [Fact]
    public void Migration_IsTotal_OverShapesWithoutTheRenamedKeys()
    {
        // Pure and total (doc 01 §5): a document with none of the renamed keys migrates
        // unchanged rather than throwing.
        var bytes = """{"schema_version":"1.0","engagement_id":"eng-1","client_id":"c-1","workflow_ids":[],"status":"active"}"""u8.ToArray();

        using var document = JsonDocument.Parse(bytes);
        var node = System.Text.Json.Nodes.JsonNode.Parse(document.RootElement.GetRawText())!;

        ArtifactVocabularyMigration.Rename(node); // must not throw

        Assert.Equal("eng-1", node["engagement_id"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("execution_snapshot.v1.json", "execution_snapshot.json")]
    [InlineData("execution_snapshot_host_build.v1.json", "execution_snapshot_host_build.json")]
    [InlineData("execution_snapshot_initiated.v1.json", "execution_snapshot_initiated.json")]
    [InlineData("execution_snapshot_paused_on_failure.v1.json", "execution_snapshot_paused_on_failure.json")]
    [InlineData("execution_snapshot_skipped.v1.json", "execution_snapshot_skipped.json")]
    [InlineData("workflow_definition.v1.json", "workflow_definition.json")]
    public void EveryRenamedWireKey_IsCoveredByTheAdapter(string legacyGolden, string currentGolden)
    {
        // The guard that found the real bug during this task: `trigger_section_keys` contains
        // "section_key" as a substring but is a distinct wire key, so a hand-written rename
        // table missed it and CascadeCheckNode failed to rehydrate. Diffing the v1 and v2
        // golden key sets proves the table is complete rather than plausible — any future
        // vocabulary change fails here until its adapter entry exists.
        var legacyKeys = WireKeys(JsonDocument.Parse(LegacyBytes(legacyGolden)).RootElement);
        var currentKeys = WireKeys(JsonDocument.Parse(File.ReadAllBytes(GoldenPath(currentGolden))).RootElement);

        var removed = legacyKeys.Except(currentKeys).ToList();
        var migrated = MigratedKeys(JsonDocument.Parse(LegacyBytes(legacyGolden)).RootElement);

        Assert.All(removed, key => Assert.DoesNotContain(key, migrated));
        Assert.Empty(migrated.Except(currentKeys));
    }

    /// <summary>Every property name appearing anywhere in <paramref name="element"/>.</summary>
    private static HashSet<string> WireKeys(JsonElement element)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        Walk(element, keys);
        return keys;

        static void Walk(JsonElement node, HashSet<string> acc)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject())
                    {
                        acc.Add(property.Name);
                        Walk(property.Value, acc);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in node.EnumerateArray())
                    {
                        Walk(item, acc);
                    }

                    break;
            }
        }
    }

    /// <summary>The key set the adapter produces from legacy bytes.</summary>
    private static HashSet<string> MigratedKeys(JsonElement legacy)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(legacy.GetRawText())!;
        ArtifactVocabularyMigration.Rename(node);
        return WireKeys(JsonDocument.Parse(node.ToJsonString()).RootElement);
    }

    private static byte[] LegacyBytes(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "legacy-v1", fileName));

    private static string GoldenPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "GoldenFiles", fileName);
}
