using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// Stored definitions never migrated: snapshots rehydrate through ContractMigrator, definitions
/// deserialized straight through, so pre-rename bytes came back with every node's
/// <c>artifact_key</c> null. The old <c>section_key</c> was an unrecognised property and was
/// simply dropped — silently, which is what made it expensive.
/// </summary>
public sealed class MigratingWorkflowDefinitionConverterTests
{
    /// <summary>A schema-1.0 draft document exactly as one is stored: nodes carry section_key.</summary>
    private const string StoredV1Document = """
        {
          "id": "wf-x:draft",
          "workflowId": "wf-x",
          "state": "draft",
          "baseVersion": 0,
          "draftRevision": "rev-1",
          "definition": {
            "schema_version": "1.0",
            "workflow_id": "wf-x",
            "definition_version": 1,
            "engagement_type": "support-triage",
            "name": "Stale draft",
            "definition_hash": "sha256:stale",
            "mode": "one_shot",
            "nodes": [
              { "node_type": "agent_task", "node_id": "gen-scope", "section_key": "scope",
                "role": "deep-reasoning", "instructions_ref": "instructions/transform.md",
                "input_contract_type": "BriefArtifact", "output_contract_type": "SummaryArtifact",
                "context_request": { "schema_version": "1.0", "engagement_id": "eng-1",
                                     "agent_role": "deep-reasoning",
                                     "baseline_components": ["firm-standards"],
                                     "dynamic_fields": ["case_summary"],
                                     "requires_real_time": false, "real_time_sources": [] } },
              { "node_type": "human_gate", "node_id": "gate-1", "gate_kind": "business",
                "approver_roles": ["business-approver"], "rollback_to_node_id": "gen-scope",
                "prompt_template": "Approve?", "timeout_minutes": 60 }
            ],
            "edges": [
              { "kind": "control", "from_node_id": "gen-scope", "to_node_id": "gate-1" }
            ]
          },
          "lastEditedBy": "designer-1",
          "lastEditedUtc": "2026-08-01T00:00:00.000Z"
        }
        """;

    private static DefinitionDraftDocument ReadStoredDraft() =>
        JsonSerializer.Deserialize<DefinitionDraftDocument>(StoredV1Document, CanonicalProfile.Options)!;

    [Fact]
    public void StoredV1Definition_ReadsForward_WithArtifactKeysIntact()
    {
        var draft = ReadStoredDraft();

        var agentTask = Assert.IsType<AgentTaskNode>(draft.Definition.Nodes[0]);
        Assert.Equal("scope", agentTask.ArtifactKey);
    }

    [Fact]
    public async Task StoredV1Definition_NoLongerFailsTheRuleThatMadeItUnrepairable()
    {
        // The reported symptom: a gate whose rollback target "produces no artifact_key". The
        // target did carry one — under its old name — and the finding read as a content problem
        // the designer could not fix, because its merge is validation-gated and so could never
        // write the repair.
        var draft = ReadStoredDraft();

        var findings = await new HitlRollbackTargetValidRule()
            .EvaluateAsync(new DefinitionValidationContext(draft.Definition), CancellationToken.None);

        Assert.DoesNotContain(findings, f => f.Message.Contains("artifact_key", StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentSchemaDefinition_ReadsUnchanged()
    {
        var current = StoredV1Document
            .Replace("\"schema_version\": \"1.0\"", "\"schema_version\": \"2.0\"", StringComparison.Ordinal)
            .Replace("\"section_key\"", "\"artifact_key\"", StringComparison.Ordinal);

        var draft = JsonSerializer.Deserialize<DefinitionDraftDocument>(current, CanonicalProfile.Options)!;

        Assert.Equal("scope", Assert.IsType<AgentTaskNode>(draft.Definition.Nodes[0]).ArtifactKey);
    }

    [Fact]
    public void Write_EmitsCurrentSchema_SoAMigratedDocumentIsSavedForward()
    {
        // Reads migrate; writes always emit current. A stale draft therefore becomes current the
        // next time it is saved, rather than being rewritten behind the caller's back.
        var draft = ReadStoredDraft();

        var json = JsonSerializer.Serialize(draft, CanonicalProfile.Options);

        Assert.Contains("\"artifact_key\":\"scope\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("section_key", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadMigrated_StaleProposalJson_MigratesToo()
    {
        using var document = JsonDocument.Parse(StoredV1Document);
        var definitionJson = document.RootElement.GetProperty("definition").GetRawText();

        var definition = MigratingWorkflowDefinitionConverter.ReadMigrated(definitionJson, CanonicalProfile.Options);

        Assert.Equal("scope", Assert.IsType<AgentTaskNode>(definition!.Nodes[0]).ArtifactKey);
    }

    [Fact]
    public void ReadMigrated_JsonNull_ReturnsNull() =>
        Assert.Null(MigratingWorkflowDefinitionConverter.ReadMigrated("null", CanonicalProfile.Options));

    [Fact]
    public void UnadaptableVersion_DeserializesWithNullArtifactKeys_AndIsProbeable()
    {
        // S13.34. Two assertions, deliberately in one test because they are the two halves of the
        // defect.
        //
        // First half — today's behaviour, pinned not endorsed: a stored version with no
        // registered adapter deserializes straight through. section_key is an unrecognised
        // property, so it is dropped and ArtifactKey comes back null, with no log, no exception
        // and no finding. The nulled field then surfaces as an ordinary content error from
        // HitlRollbackTargetValidRule ("rollback target … produces no section (no artifact_key)")
        // and the designer debugs a phantom content problem. The fix must NOT change this read
        // behaviour — a definition that cannot be read faithfully must not be silently guessed at.
        //
        // Second half — what makes the fix possible: the stored version must remain recoverable
        // from the raw bytes. ArtifactVocabularyMigration.Migrate overwrites schema_version before
        // deserializing, so the deserialized definition's own SchemaVersion can never answer the
        // question; only a probe of the stored JSON can.
        var storedJson = StoredV1Document.Replace(
            "\"schema_version\": \"1.0\"", "\"schema_version\": \"0.9\"", StringComparison.Ordinal);

        var draft = JsonSerializer.Deserialize<DefinitionDraftDocument>(storedJson, CanonicalProfile.Options)!;

        Assert.Null(Assert.IsType<AgentTaskNode>(draft.Definition.Nodes[0]).ArtifactKey);

        using var document = JsonDocument.Parse(storedJson);
        var definitionJson = document.RootElement.GetProperty("definition").GetRawText();

        Assert.Equal("0.9", MigratingWorkflowDefinitionConverter.ProbeStoredSchemaVersion(definitionJson));
    }

    [Fact]
    public void ProbeStoredSchemaVersion_AdaptedDocument_ReportsTheStoredVersion_NotTheMigratedOne()
    {
        // The decisive constraint: Migrate stamps RenamedSchemaVersion onto the node before
        // deserializing, so the definition that comes back says "2.0" however old the bytes were.
        // The probe must read the bytes, not the product of the migration.
        using var document = JsonDocument.Parse(StoredV1Document);
        var definitionJson = document.RootElement.GetProperty("definition").GetRawText();

        Assert.Equal("1.0", MigratingWorkflowDefinitionConverter.ProbeStoredSchemaVersion(definitionJson));
        Assert.Equal("2.0", ReadStoredDraft().Definition.SchemaVersion);
    }

    [Fact]
    public void ProbeStoredSchemaVersion_NoSchemaVersionProperty_ReturnsNull() =>
        // Absent means unknown, not "current" — the rule emits nothing for null and must never
        // be fed a confident wrong answer.
        Assert.Null(MigratingWorkflowDefinitionConverter.ProbeStoredSchemaVersion("{\"workflow_id\":\"wf-x\"}"));
}
