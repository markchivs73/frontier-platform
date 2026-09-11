namespace Frontier.Platform.Workflow.Model.Tests.Serialization;

/// <summary>
/// S13.34: the classification behind the <c>schema.version-supported</c> rule. A stored schema
/// version is one of three things — current (read as-is), adapted (a registered adapter carried
/// it forward), or unsupported (nothing can read it faithfully, so the fields it carries are
/// silently dropped).
///
/// <para>This lives in the model because the model is where the answer is knowable:
/// <see cref="ArtifactVocabularyMigration.Migrate{T}"/> <b>overwrites</b> <c>schema_version</c>
/// before deserializing, so by the time a definition reaches the compiler its own
/// <see cref="WorkflowDefinition.SchemaVersion"/> always reads current and the stored version is
/// gone. Nothing downstream can classify what it can no longer see.</para>
/// </summary>
public sealed class DefinitionSchemaCompatibilityTests
{
    [Fact]
    public void CurrentVersion_ClassifiesAsCurrent() =>
        Assert.Equal(SchemaCompatibility.Current, DefinitionSchemaCompatibility.Classify("2.0"));

    [Fact]
    public void PreRenameVersion_ClassifiesAsAdapted() =>
        // 1.0 is exactly the version ArtifactVocabularyMigration.DefinitionAdapters covers —
        // the classification must be derived from that table, not from a second hand-maintained
        // list that can drift away from it.
        Assert.Equal(
            SchemaCompatibility.Adapted,
            DefinitionSchemaCompatibility.Classify(ArtifactVocabularyMigration.PreRenameSchemaVersion));

    [Fact]
    public void UnknownOlderMajor_ClassifiesAsUnsupported() =>
        // An older major with no registered adapter: today this deserializes straight through and
        // its unrecognised keys vanish. That is the defect — it must classify as unsupported.
        Assert.Equal(SchemaCompatibility.Unsupported, DefinitionSchemaCompatibility.Classify("0.9"));

    [Fact]
    public void NewerMajorThanCurrent_ClassifiesAsUnsupported() =>
        // The downgrade case: a draft written by a later build and read by this one. There is no
        // backward adapter and there never will be — this build cannot know what 3.0 added, so
        // reading it drops exactly the fields it does not recognise. Treating "not older" as
        // "fine" is how a downgrade silently corrupts a draft.
        Assert.Equal(SchemaCompatibility.Unsupported, DefinitionSchemaCompatibility.Classify("3.0"));

    [Fact]
    public void Garbage_ClassifiesAsUnsupported() =>
        Assert.Equal(SchemaCompatibility.Unsupported, DefinitionSchemaCompatibility.Classify("not-a-version"));

    [Fact]
    public void TheCurrentVersionIsTheOneNewDefinitionsCarry()
    {
        // Pins classification to the type's own default rather than a literal that could be
        // updated in one place and not the other at the next schema bump.
        var fresh = new WorkflowDefinition
        {
            WorkflowId = "wf-x",
            DefinitionVersion = 1,
            EngagementType = "support-triage",
            Name = "Fresh",
            DefinitionHash = "sha256:x",
            Nodes = [],
            Edges = [],
            Mode = ExecutionMode.OneShot,
        };

        Assert.Equal(SchemaCompatibility.Current, DefinitionSchemaCompatibility.Classify(fresh.SchemaVersion));
    }
}
