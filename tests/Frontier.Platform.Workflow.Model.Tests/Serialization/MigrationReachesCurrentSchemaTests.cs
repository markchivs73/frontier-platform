using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Model.Tests.Serialization;

/// <summary>
/// E15b: the guarantee an execution paused across <em>several</em> schema versions depends on.
/// <para>
/// <see cref="ContractMigrator"/> does not chain. It looks the stored <c>schema_version</c> up
/// once and calls that one adapter, so **every registered adapter must reach the current schema
/// by itself**. When a contract goes to 3.0, the adapter written for 1.0 back when current was
/// 2.0 does not stop working — it keeps producing a 2.0-shaped object, deserialized as the 3.0
/// type with the new fields left at their defaults. Nothing throws. An execution paused at a gate
/// long enough to cross two versions is exactly what meets that.
/// </para>
/// <para>
/// The existing adapter tests cannot catch it, and it is worth being precise about why:
/// <c>ArtifactVocabularyMigrationTests</c> asserts the migrated version equals
/// <c>ArtifactVocabularyMigration.RenamedSchemaVersion</c> — the adapter's <em>own</em> constant.
/// At 3.0 that constant still reads "2.0", the assertion still passes, and the adapter is wrong.
/// A test that compares a thing to itself survives the change it looks like it guards.
/// </para>
/// <para>
/// So the version is taken from the contract type instead, by reading a current golden with its
/// <c>schema_version</c> removed and letting the property initializer answer. Nothing here needs
/// updating at the next bump; it just starts failing until each adapter is brought forward.
/// </para>
/// </summary>
public sealed class MigrationReachesCurrentSchemaTests
{
    [Theory]
    [InlineData("execution_snapshot.v1.json", "execution_snapshot.json")]
    [InlineData("execution_snapshot_host_build.v1.json", "execution_snapshot_host_build.json")]
    [InlineData("execution_snapshot_initiated.v1.json", "execution_snapshot_initiated.json")]
    [InlineData("execution_snapshot_paused_on_failure.v1.json", "execution_snapshot_paused_on_failure.json")]
    [InlineData("execution_snapshot_skipped.v1.json", "execution_snapshot_skipped.json")]
    public void SnapshotAdapter_ReachesTheTypesCurrentSchemaVersion(string legacyGolden, string currentGolden)
    {
        var migrated = ContractMigrator.Rehydrate(
            LegacyBytes(legacyGolden), CanonicalProfile.Options, ArtifactVocabularyMigration.SnapshotAdapters);

        Assert.Equal(CurrentSchemaVersionOf<ExecutionSnapshot>(currentGolden), migrated.SchemaVersion);
    }

    [Theory]
    [InlineData("workflow_definition.v1.json", "workflow_definition.json")]
    public void DefinitionAdapter_ReachesTheTypesCurrentSchemaVersion(string legacyGolden, string currentGolden)
    {
        var migrated = ContractMigrator.Rehydrate(
            LegacyBytes(legacyGolden), CanonicalProfile.Options, ArtifactVocabularyMigration.DefinitionAdapters);

        Assert.Equal(CurrentSchemaVersionOf<WorkflowDefinition>(currentGolden), migrated.SchemaVersion);
    }

    /// <summary>
    /// Every key in every adapter table must be a version the type has genuinely moved on from.
    /// An adapter registered for the current version would be asked to migrate bytes that need no
    /// migration — a no-op at best, and at worst a rewrite applied to already-correct bytes.
    /// </summary>
    [Fact]
    public void NoAdapterIsRegisteredForTheCurrentSchemaVersion()
    {
        Assert.DoesNotContain(
            CurrentSchemaVersionOf<ExecutionSnapshot>("execution_snapshot.json"),
            ArtifactVocabularyMigration.SnapshotAdapters.Keys);

        Assert.DoesNotContain(
            CurrentSchemaVersionOf<WorkflowDefinition>("workflow_definition.json"),
            ArtifactVocabularyMigration.DefinitionAdapters.Keys);
    }

    /// <summary>
    /// Both tables must cover the same stored versions. They describe one wire break, so a version
    /// adapted for definitions and not for snapshots would leave half a paused execution readable.
    /// </summary>
    [Fact]
    public void BothTablesCoverTheSameStoredVersions()
    {
        Assert.Equal(
            ArtifactVocabularyMigration.SnapshotAdapters.Keys.Order(StringComparer.Ordinal),
            ArtifactVocabularyMigration.DefinitionAdapters.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The premise of the tests above: the version really is read off the type and not off the
    /// golden. If the golden kept its own <c>schema_version</c>, each assertion would be comparing
    /// stored bytes to themselves and would hold no matter what the adapter produced.
    /// </summary>
    [Fact]
    public void TheCurrentVersionComesFromTheTypeNotTheStoredBytes()
    {
        // Doctored to claim a version the type has never had. If the helper were reading the
        // bytes, every assertion above would be comparing stored data to itself and would hold
        // whatever the adapter produced.
        var doctored = JsonNode.Parse(File.ReadAllText(GoldenPath("execution_snapshot.json")))!;
        doctored["schema_version"] = "99.9";

        var fromType = RemoveVersion(doctored).Deserialize<ExecutionSnapshot>(CanonicalProfile.Options)!.SchemaVersion;

        Assert.NotEqual("99.9", fromType);
        Assert.Equal(CurrentSchemaVersionOf<ExecutionSnapshot>("execution_snapshot.json"), fromType);
    }

    /// <summary>
    /// The contract type's own current version, obtained by deserializing a current golden with
    /// <c>schema_version</c> removed so the property initializer supplies it. Deliberately not a
    /// constant: a constant is the thing that goes stale at the next bump.
    /// </summary>
    internal static string CurrentSchemaVersionOf<T>(string currentGolden)
        where T : IVersionedContract
    {
        var node = RemoveVersion(JsonNode.Parse(File.ReadAllText(GoldenPath(currentGolden)))!);

        return node.Deserialize<T>(CanonicalProfile.Options)!.SchemaVersion;
    }

    internal static JsonNode RemoveVersion(JsonNode node)
    {
        node.AsObject().Remove("schema_version");
        return node;
    }

    internal static byte[] LegacyBytes(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "legacy-v1", fileName));

    internal static string GoldenPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "GoldenFiles", fileName);
}
