using System.Text.Json;
using Frontier.Platform.Serialization;

using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Phase 1 proposal merge service: applies agent proposals to drafts with conflict detection.
/// Doc 14 §3: fetch current draft, detect conflicts, run validation, save with ETag guard.
/// </summary>
public sealed class ProposalMergeService : IProposalMergeService
{
    private readonly IDefinitionStore _store;
    private readonly IDefinitionCompiler _compiler;
    private readonly INodeDiffService _diffService;

    public ProposalMergeService(
        IDefinitionStore store,
        IDefinitionCompiler compiler,
        INodeDiffService diffService)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(diffService);
        _store = store;
        _compiler = compiler;
        _diffService = diffService;
    }

    public async Task<ProposalMergeOutcome> ApplyProposalAsync(
        string workflowId,
        string proposedDefinitionJson,
        string agentReasoning,
        string designerId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(workflowId);
        ArgumentNullException.ThrowIfNullOrEmpty(proposedDefinitionJson);
        ArgumentNullException.ThrowIfNullOrEmpty(agentReasoning);
        ArgumentNullException.ThrowIfNullOrEmpty(designerId);

        // Phase 1: parse proposal
        WorkflowDefinition? proposedDefinition;
        try
        {
            proposedDefinition = JsonSerializer.Deserialize<WorkflowDefinition>(
                proposedDefinitionJson,
                CanonicalProfile.Options);
        }
        catch (JsonException)
        {
            // Malformed JSON — treat as validation failure
            return new ProposalMergeOutcomeValidationBlocked
            {
                DraftRevisionAfterMerge = "merge-failed",
                BlockingFindings = new[]
                {
                    new ValidationFinding(
                        RuleId: "proposal-parse-failed",
                        Severity: ValidationSeverity.Error,
                        Message: "Agent proposal JSON is malformed")
                }.ToList().AsReadOnly()
            };
        }

        if (proposedDefinition == null)
        {
            return new ProposalMergeOutcomeValidationBlocked
            {
                DraftRevisionAfterMerge = "merge-failed",
                BlockingFindings = new[]
                {
                    new ValidationFinding(
                        RuleId: "proposal-null",
                        Severity: ValidationSeverity.Error,
                        Message: "Agent proposal deserialized to null")
                }.ToList().AsReadOnly()
            };
        }

        // Fetch current draft
        var draft = await _store.GetDraftAsync(workflowId, ct);
        if (draft == null)
        {
            return new ProposalMergeOutcomeValidationBlocked
            {
                DraftRevisionAfterMerge = "no-draft",
                BlockingFindings = new[]
                {
                    new ValidationFinding(
                        RuleId: "draft-not-found",
                        Severity: ValidationSeverity.Error,
                        Message: $"No draft found for workflow {workflowId}")
                }.ToList().AsReadOnly()
            };
        }

        // Detect conflicts (both designer and agent modified the same nodes)
        var diff = _diffService.Compute(draft.Definition, proposedDefinition);
        var designerEditNodeIds = new HashSet<string>(diff.NodesModified);
        var agentEditNodeIds = new HashSet<string>(diff.NodesAdded.Concat(diff.NodesModified));
        var conflicts = designerEditNodeIds.Intersect(agentEditNodeIds).ToList();

        if (conflicts.Count > 0)
        {
            // Conflict: return both versions for designer to resolve
            return new ProposalMergeOutcomeConflict
            {
                DraftRevisionAfterMerge = draft.DraftRevision,
                Conflicts = conflicts.Select(nodeId => new NodeConflict
                {
                    NodeId = nodeId,
                    DesignerVersion = SerializeNode(draft.Definition.Nodes.FirstOrDefault(n => n.NodeId == nodeId)),
                    AgentProposedVersion = SerializeNode(proposedDefinition.Nodes.FirstOrDefault(n => n.NodeId == nodeId))
                }).ToList().AsReadOnly(),
                DesignerEdit = draft.Definition,
                AgentProposal = proposedDefinition
            };
        }

        // No conflicts — run validation on the proposal
        var findings = _compiler.ValidateStructural(proposedDefinition);
        var blockingFindings = findings.Where(f => f.Severity == ValidationSeverity.Error).ToList();

        if (blockingFindings.Count > 0)
        {
            return new ProposalMergeOutcomeValidationBlocked
            {
                DraftRevisionAfterMerge = draft.DraftRevision,
                BlockingFindings = blockingFindings.AsReadOnly()
            };
        }

        // Merge: update draft with proposed definition
        var newRevision = GenerateNewRevision(draft.DraftRevision);
        var updatedDraft = draft with
        {
            DraftRevision = newRevision,
            Definition = proposedDefinition,
            LastEditedBy = designerId,
            LastEditedUtc = DateTime.UtcNow
        };

        await _store.SaveDraftAsync(workflowId, updatedDraft, "no-etag-check", ct);

        return new ProposalMergeOutcomeMerged
        {
            DraftRevisionAfterMerge = newRevision,
            UpdatedDraft = updatedDraft
        };
    }

    public async Task<ProposalMergeOutcome> ApplyApprovedChangesAsync(
        string workflowId,
        IReadOnlyList<string> approvedChangeIds,
        string expectedRevision,
        string designerId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(workflowId);
        ArgumentNullException.ThrowIfNull(approvedChangeIds);
        ArgumentNullException.ThrowIfNullOrEmpty(expectedRevision);
        ArgumentNullException.ThrowIfNullOrEmpty(designerId);

        var draft = await _store.GetDraftAsync(workflowId, ct);
        if (draft == null) return Blocked("draft-not-found", $"No draft found for workflow {workflowId}", "no-draft");

        // Stale base → conflict (doc 14 §4.2; surfaces as 409 to the UI).
        if (draft.DraftRevision != expectedRevision)
        {
            return new ProposalMergeOutcomeConflict
            {
                DraftRevisionAfterMerge = draft.DraftRevision,
                Conflicts = [],
                DesignerEdit = draft.Definition,
                AgentProposal = draft.Definition,
            };
        }

        var proposed = await LoadLatestProposalAsync(workflowId, ct);
        if (proposed == null) return Blocked("no-proposal", "No agent proposal is available to merge", draft.DraftRevision);

        var merged = ApplyApproved(draft.Definition, proposed, new HashSet<string>(approvedChangeIds, StringComparer.Ordinal));

        var blocking = _compiler.ValidateStructural(merged).Where(f => f.Severity == ValidationSeverity.Error).ToList();
        if (blocking.Count > 0)
        {
            return new ProposalMergeOutcomeValidationBlocked
            {
                DraftRevisionAfterMerge = draft.DraftRevision,
                BlockingFindings = blocking.AsReadOnly(),
            };
        }

        var newRevision = GenerateNewRevision(draft.DraftRevision);
        var updatedDraft = draft with
        {
            DraftRevision = newRevision,
            Definition = merged,
            LastEditedBy = designerId,
            LastEditedUtc = DateTime.UtcNow,
        };
        await _store.SaveDraftAsync(workflowId, updatedDraft, expectedRevision, ct);

        return new ProposalMergeOutcomeMerged { DraftRevisionAfterMerge = newRevision, UpdatedDraft = updatedDraft };
    }

    /// <summary>Loads and deserializes the most recent turn's proposed definition, or null if none.</summary>
    internal async Task<WorkflowDefinition?> LoadLatestProposalAsync(string workflowId, CancellationToken ct)
    {
        var turns = await _store.GetAllDesignTurnsAsync(workflowId, ct);
        var latest = turns
            .Where(t => !string.IsNullOrEmpty(t.AgentProposalJson))
            .OrderByDescending(t => t.TurnNumber)
            .FirstOrDefault();
        if (latest?.AgentProposalJson is null) return null;

        try
        {
            return JsonSerializer.Deserialize<WorkflowDefinition>(latest.AgentProposalJson, CanonicalProfile.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Applies the approved subset of changes from <paramref name="proposed"/> onto <paramref name="current"/>.</summary>
    internal static WorkflowDefinition ApplyApproved(
        WorkflowDefinition current,
        WorkflowDefinition proposed,
        HashSet<string> approved) =>
        current with
        {
            Nodes = MergeNodes(current, proposed, approved),
            Edges = MergeEdges(current, proposed, approved),
        };

    private static List<WorkflowNode> MergeNodes(WorkflowDefinition current, WorkflowDefinition proposed, HashSet<string> approved)
    {
        var nodes = current.Nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
        foreach (var n in proposed.Nodes)
        {
            if (approved.Contains(ProposalChangeSetBuilder.NodeChangeId(ProposalChangeSetBuilder.Added, n.NodeId)) ||
                approved.Contains(ProposalChangeSetBuilder.NodeChangeId(ProposalChangeSetBuilder.Modified, n.NodeId)))
                nodes[n.NodeId] = n;
        }

        foreach (var id in current.Nodes.Select(n => n.NodeId).ToList())
        {
            if (approved.Contains(ProposalChangeSetBuilder.NodeChangeId(ProposalChangeSetBuilder.Removed, id)))
                nodes.Remove(id);
        }

        return nodes.Values.ToList();
    }

    private static List<WorkflowEdge> MergeEdges(WorkflowDefinition current, WorkflowDefinition proposed, HashSet<string> approved)
    {
        var edges = current.Edges.ToDictionary(ProposalChangeSetBuilder.EdgeKey, StringComparer.Ordinal);
        foreach (var e in proposed.Edges)
        {
            if (approved.Contains(ProposalChangeSetBuilder.EdgeChangeId(ProposalChangeSetBuilder.Added, ProposalChangeSetBuilder.EdgeKey(e))))
                edges[ProposalChangeSetBuilder.EdgeKey(e)] = e;
        }

        foreach (var e in current.Edges.ToList())
        {
            if (approved.Contains(ProposalChangeSetBuilder.EdgeChangeId(ProposalChangeSetBuilder.Removed, ProposalChangeSetBuilder.EdgeKey(e))))
                edges.Remove(ProposalChangeSetBuilder.EdgeKey(e));
        }

        return edges.Values.ToList();
    }

    private static ProposalMergeOutcomeValidationBlocked Blocked(string ruleId, string message, string revision) => new()
    {
        DraftRevisionAfterMerge = revision,
        BlockingFindings = new[] { new ValidationFinding(ruleId, ValidationSeverity.Error, message) }.ToList().AsReadOnly(),
    };

    private static string SerializeNode(WorkflowNode? node)
    {
        if (node == null)
            return "null";

        try
        {
            return JsonSerializer.Serialize(node, CanonicalProfile.Options);
        }
        catch (JsonException)
        {
            return "serialization-error";
        }
    }

    private static string GenerateNewRevision(string currentRevision)
    {
        // Phase 1: simple numeric revision (rev-1, rev-2, ...)
        if (currentRevision.StartsWith("rev-", StringComparison.Ordinal))
        {
            if (int.TryParse(currentRevision.AsSpan(4), System.Globalization.CultureInfo.InvariantCulture, out var num))
            {
                return $"rev-{num + 1}";
            }
        }

        return $"rev-{Guid.NewGuid():N}";
    }
}
