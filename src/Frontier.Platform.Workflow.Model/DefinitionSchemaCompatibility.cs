namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// How a stored schema version relates to the one this build reads (S13.34).
/// </summary>
public enum SchemaCompatibility
{
    /// <summary>The same major as the current schema: read as-is, nothing is dropped.</summary>
    Current,

    /// <summary>A registered adapter carried the stored bytes forward (<see cref="ArtifactVocabularyMigration.DefinitionAdapters"/>).</summary>
    Adapted,

    /// <summary>Nothing can read these bytes faithfully — unrecognised keys are dropped silently.</summary>
    Unsupported
}

/// <summary>
/// Classifies the <em>stored</em> schema version of a workflow definition (S13.34, ADR-PA23).
///
/// <para>This lives in the model because the model is where the answer is knowable:
/// <see cref="ArtifactVocabularyMigration.Migrate{T}"/> overwrites <c>schema_version</c> before
/// deserializing, so a definition that has been read always reports the current version and the
/// stored one is gone. Only a probe of the stored bytes
/// (<see cref="MigratingWorkflowDefinitionConverter.ProbeStoredSchemaVersion"/>) can answer it.</para>
/// </summary>
public static class DefinitionSchemaCompatibility
{
    /// <summary>The schema version new definitions carry — the same constant <see cref="WorkflowDefinition.SchemaVersion"/> defaults to, never a second literal.</summary>
    public static string CurrentSchemaVersion => ArtifactVocabularyMigration.RenamedSchemaVersion;

    /// <summary>
    /// Classifies <paramref name="storedSchemaVersion"/>. A registered adapter wins first; otherwise
    /// the same major as <see cref="CurrentSchemaVersion"/> is <see cref="SchemaCompatibility.Current"/>
    /// (minor bumps are additive by convention and already deserialize normally) and everything else —
    /// an older major with no adapter, a newer major written by a later build, an unparseable value,
    /// or no version at all — is <see cref="SchemaCompatibility.Unsupported"/>.
    /// </summary>
    public static SchemaCompatibility Classify(string? storedSchemaVersion)
    {
        if (storedSchemaVersion is null)
        {
            return SchemaCompatibility.Unsupported;
        }

        if (ArtifactVocabularyMigration.DefinitionAdapters.ContainsKey(storedSchemaVersion))
        {
            return SchemaCompatibility.Adapted;
        }

        return Version.TryParse(storedSchemaVersion, out var stored)
            && Version.TryParse(CurrentSchemaVersion, out var current)
            && stored.Major == current.Major
                ? SchemaCompatibility.Current
                : SchemaCompatibility.Unsupported;
    }
}
