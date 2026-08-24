using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.35: a definition rides inline in the orchestration input (ADR-2), so it lives in durable
/// history and is rehydrated on every replay. Before this, a schema change meant a running
/// execution replayed with null artifact keys — making different scheduling decisions than the
/// run it was replaying, which is the one thing replay may never do.
///
/// Migration here is deterministic by construction: the recorded bytes never change, and the
/// adapter is a pure total function of them. These tests pin that, because "we added a
/// transformation to the replay path" is exactly the sentence that should make a reader nervous.
/// </summary>
public sealed class ReplayedDefinitionMigrationTests
{
    /// <summary>Orchestration input as it would have been recorded before the artifact rename.</summary>
    private const string RecordedV1Input = """
        {
          "definition": {
            "schema_version": "1.0",
            "workflow_id": "wf-x",
            "definition_version": 3,
            "engagement_type": "support-triage",
            "name": "Recorded before the rename",
            "definition_hash": "sha256:recorded",
            "mode": "one_shot",
            "nodes": [
              { "node_type": "agent_task", "node_id": "step-1", "section_key": "scope",
                "role": "deep-reasoning", "instructions_ref": "instructions/transform.md",
                "input_contract_type": "BriefArtifact", "output_contract_type": "SummaryArtifact",
                "context_request": { "schema_version": "1.0", "engagement_id": "eng-1",
                                     "agent_role": "deep-reasoning",
                                     "baseline_components": ["firm-standards"],
                                     "dynamic_fields": [], "requires_real_time": false,
                                     "real_time_sources": [] } }
            ],
            "edges": []
          },
          "engagement_id": "eng-1",
          "workflow_id": "wf-x"
        }
        """;

    private static GraphOrchestratorInput Rehydrate() =>
        JsonSerializer.Deserialize<GraphOrchestratorInput>(RecordedV1Input, CanonicalProfile.Options)!;

    [Fact]
    public void RecordedV1Input_RehydratesWithArtifactKeys()
    {
        var input = Rehydrate();

        Assert.Equal("scope", Assert.IsType<AgentTaskNode>(input.Definition.Nodes[0]).ArtifactKey);
    }

    [Fact]
    public void Rehydration_IsStableAcrossReplays()
    {
        // The property replay depends on: identical bytes in, identical decisions out. A
        // migration that varied per call would be worse than no migration at all.
        var first = JsonSerializer.Serialize(Rehydrate().Definition, CanonicalProfile.Options);
        var second = JsonSerializer.Serialize(Rehydrate().Definition, CanonicalProfile.Options);
        var third = JsonSerializer.Serialize(Rehydrate().Definition, CanonicalProfile.Options);

        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    [Fact]
    public void MigratedDefinition_KeepsTheValuesTheOriginalRunScheduledOn()
    {
        // Migration restores the run's own semantics rather than changing them: the value was
        // always "scope", only the property carrying it was renamed. That is why this is safe on
        // a replay path and a semantic change would not be.
        using var document = JsonDocument.Parse(RecordedV1Input);
        var recorded = document.RootElement.GetProperty("definition").GetProperty("nodes")[0]
            .GetProperty("section_key").GetString();

        Assert.Equal(recorded, Assert.IsType<AgentTaskNode>(Rehydrate().Definition.Nodes[0]).ArtifactKey);
    }

    [Fact]
    public void CurrentSchemaInput_RehydratesUnchanged()
    {
        var current = RecordedV1Input
            .Replace("\"schema_version\": \"1.0\"", "\"schema_version\": \"2.0\"", StringComparison.Ordinal)
            .Replace("\"section_key\"", "\"artifact_key\"", StringComparison.Ordinal);

        var input = JsonSerializer.Deserialize<GraphOrchestratorInput>(current, CanonicalProfile.Options)!;

        Assert.Equal("scope", Assert.IsType<AgentTaskNode>(input.Definition.Nodes[0]).ArtifactKey);
    }
}
