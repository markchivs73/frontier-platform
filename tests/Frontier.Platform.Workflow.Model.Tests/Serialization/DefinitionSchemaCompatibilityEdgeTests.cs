namespace Frontier.Platform.Workflow.Model.Tests.Serialization;

/// <summary>
/// S13.34, added alongside the specification suite: the edges of the classification and the probe
/// that the specification tests do not reach. Nothing here relaxes anything they pin.
/// </summary>
public sealed class DefinitionSchemaCompatibilityEdgeTests
{
    [Fact]
    public void NoStoredVersion_ClassifiesAsUnsupported() =>
        // Defence in depth. The rule short-circuits on null before classifying ("not probed" is
        // not a finding), so this is the answer for a caller that does classify null: a version
        // that cannot be read is not one that can be assumed current.
        Assert.Equal(SchemaCompatibility.Unsupported, DefinitionSchemaCompatibility.Classify(null));

    [Fact]
    public void CurrentSchemaVersion_IsTheConstantTheContractDefaultsTo() =>
        Assert.Equal(
            ArtifactVocabularyMigration.RenamedSchemaVersion, DefinitionSchemaCompatibility.CurrentSchemaVersion);

    [Fact]
    public void ProbeStoredSchemaVersion_NonObjectJson_ReturnsNull() =>
        // A definition node that is an array or a bare scalar carries no version to probe.
        Assert.Null(MigratingWorkflowDefinitionConverter.ProbeStoredSchemaVersion("[]"));

    [Fact]
    public void ProbeStoredSchemaVersion_NonStringSchemaVersion_ReturnsNull() =>
        Assert.Null(MigratingWorkflowDefinitionConverter.ProbeStoredSchemaVersion("{\"schema_version\":2}"));

    [Fact]
    public void ProbeStoredSchemaVersion_NullJson_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => MigratingWorkflowDefinitionConverter.ProbeStoredSchemaVersion(null!));
}
