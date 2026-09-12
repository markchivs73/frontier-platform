using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.7f: every persisted storage document must survive a write→read round-trip through the
/// canonical profile — the same <see cref="JsonSerializerOptions"/> the Cosmos client is
/// configured with (<c>UseSystemTextJsonSerializerWithOptions</c>).
///
/// The profile omits nulls when writing (hard invariant 1), so a nullable property that is also
/// <c>required</c> is written absent and then fails to deserialize — the document poisons its own
/// container. That defect shipped in <see cref="WorkflowUsageDocument.LastRunAtUtc"/> and took out
/// the whole A1 catalogue: one never-run workflow's usage sidecar made
/// <c>GET /api/workflows</c> return 500, so the page listed nothing at all.
///
/// Existing tests missed it because they stopped at the boundary — the projector's own tests
/// assert <c>LastRunAtUtc</c> is null against a *mocked* store, so nothing ever serialized the
/// document. These tests cross that boundary.
/// </summary>
public sealed class StorageDocumentRoundTripTests
{
    [Fact]
    public void WorkflowUsageDocument_NeverRunWorkflow_SurvivesWriteThenRead()
    {
        // A workflow with an active execution but no terminal run yet — exactly the shape the
        // sweep writes first, and the one that broke the catalogue.
        var written = new WorkflowUsageDocument
        {
            Id = "wf-ticket-assignment:usage",
            WorkflowId = "wf-ticket-assignment",
            LastRunAtUtc = null,
            RunCount30d = 0,
            FailureCount30d = 0,
            ActiveCount = 1,
            SweptAtUtc = new DateTime(2026, 8, 17, 20, 31, 45, DateTimeKind.Utc),
        };

        var json = JsonSerializer.Serialize(written, CanonicalProfile.Options);
        var read = JsonSerializer.Deserialize<WorkflowUsageDocument>(json, CanonicalProfile.Options);

        Assert.NotNull(read);
        Assert.Null(read!.LastRunAtUtc);
        Assert.Equal(written, read);
    }

    [Fact]
    public void WorkflowUsageDocument_NullLastRun_IsOmittedFromTheWrittenJson()
    {
        // Pins the *reason* the round-trip is fragile: the key genuinely is absent on the wire,
        // so the read side must treat absent as "never run" rather than demanding the property.
        var json = JsonSerializer.Serialize(
            new WorkflowUsageDocument
            {
                Id = "wf-x:usage",
                WorkflowId = "wf-x",
                LastRunAtUtc = null,
                RunCount30d = 0,
                FailureCount30d = 0,
                ActiveCount = 0,
                SweptAtUtc = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc),
            },
            CanonicalProfile.Options);

        Assert.DoesNotContain("lastRunAtUtc", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowUsageDocument_RanWorkflow_RoundTripsTheTimestamp()
    {
        var written = new WorkflowUsageDocument
        {
            Id = "wf-y:usage",
            WorkflowId = "wf-y",
            LastRunAtUtc = new DateTime(2026, 8, 10, 9, 30, 0, DateTimeKind.Utc),
            RunCount30d = 4,
            FailureCount30d = 1,
            ActiveCount = 2,
            SweptAtUtc = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc),
        };

        var read = JsonSerializer.Deserialize<WorkflowUsageDocument>(
            JsonSerializer.Serialize(written, CanonicalProfile.Options), CanonicalProfile.Options);

        Assert.Equal(written, read);
    }

    /// <summary>A draft document carrying the S13.34 probed stored schema version.</summary>
    private static DefinitionDraftDocument Draft(string? storedSchemaVersion) => new()
    {
        Id = "wf-x:draft",
        WorkflowId = "wf-x",
        State = "draft",
        BaseVersion = 0,
        DraftRevision = "rev-1",
        Definition = new WorkflowDefinition
        {
            WorkflowId = "wf-x",
            DefinitionVersion = 1,
            EngagementType = "support-triage",
            Name = "Stale draft",
            DefinitionHash = "sha256:stale",
            Nodes = [],
            Edges = [],
            Mode = ExecutionMode.OneShot,
        },
        StoredSchemaVersion = storedSchemaVersion,
        LastEditedBy = "designer-1",
        LastEditedUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void DefinitionDraftDocument_StoredSchemaVersion_SurvivesWriteThenRead()
    {
        // S13.34: the probed stored version is what the validation pass classifies. It has to be
        // a document field because ArtifactVocabularyMigration.Migrate overwrites schema_version
        // before deserializing — the definition itself can never report where it came from.
        var written = Draft("1.0");

        var read = JsonSerializer.Deserialize<DefinitionDraftDocument>(
            JsonSerializer.Serialize(written, CanonicalProfile.Options), CanonicalProfile.Options);

        Assert.NotNull(read);
        Assert.Equal("1.0", read!.StoredSchemaVersion);
    }

    [Fact]
    public void DefinitionDraftDocument_NoStoredSchemaVersion_RoundTrips()
    {
        // Optional and null-omitting: every document written before this field existed must still
        // read, and must read as "unknown" (null), never as a confident "current".
        var read = JsonSerializer.Deserialize<DefinitionDraftDocument>(
            JsonSerializer.Serialize(Draft(null), CanonicalProfile.Options), CanonicalProfile.Options);

        Assert.NotNull(read);
        Assert.Null(read!.StoredSchemaVersion);
    }

    [Fact]
    public void DefinitionDraftDocument_StoredSchemaVersion_DoesNotChangeTheDefinitionsCanonicalBytes()
    {
        // Hard invariant 1. The new field rides on the storage envelope; the definition's own
        // canonical bytes — and therefore its hash — must be byte-identical whether the envelope
        // carries a stored version or not. If this fails, every published definition's hash moved.
        var definitionBytes = JsonSerializer.Serialize(Draft(null).Definition, CanonicalProfile.Options);

        var withVersion = JsonDocument.Parse(JsonSerializer.Serialize(Draft("1.0"), CanonicalProfile.Options));
        var withoutVersion = JsonDocument.Parse(JsonSerializer.Serialize(Draft(null), CanonicalProfile.Options));

        Assert.Equal(definitionBytes, withVersion.RootElement.GetProperty("definition").GetRawText());
        Assert.Equal(
            withoutVersion.RootElement.GetProperty("definition").GetRawText(),
            withVersion.RootElement.GetProperty("definition").GetRawText());
        Assert.DoesNotContain(
            "schema_version\":\"1.0", withVersion.RootElement.GetProperty("definition").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowHealthDocument_HealthyVersion_SurvivesWriteThenRead()
    {
        // The catalogue reads a health sidecar on the same code path (CurrentHealthByWorkflowAsync);
        // it must not repeat the usage document's failure mode.
        var written = new WorkflowHealthDocument
        {
            Id = "wf-z:v1:health",
            WorkflowId = "wf-z",
            DefinitionVersion = 1,
            HealthStatus = "healthy",
            FailingRuleIds = [],
            FindingCount = 0,
            CheckedAtUtc = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc),
        };

        var read = JsonSerializer.Deserialize<WorkflowHealthDocument>(
            JsonSerializer.Serialize(written, CanonicalProfile.Options), CanonicalProfile.Options);

        Assert.NotNull(read);
        Assert.Equal(written.Id, read!.Id);
        Assert.Equal(written.HealthStatus, read.HealthStatus);
        Assert.Empty(read.FailingRuleIds);
        Assert.Equal(written.CheckedAtUtc, read.CheckedAtUtc);
    }
}
