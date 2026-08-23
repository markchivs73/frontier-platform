
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Proposal merge service: applies agent proposals to the draft, handles conflicts, runs validation.
/// Doc 14 §3: fetch→propose→diff→merge with ETag guards, conflict resolution per ADR-CD1/CD2.
/// </summary>
public interface IProposalMergeService
{
    /// <summary>
    /// Apply an agent proposal to the draft. Returns Merged, MergeConflict, or ValidationBlocked outcome.
    /// On conflict, includes both designer edits and agent edits for explicit selection.
    /// </summary>
    Task<ProposalMergeOutcome> ApplyProposalAsync(
        string workflowId,
        string proposedDefinitionJson,
        string agentReasoning,
        string designerId,
        CancellationToken ct);

    /// <summary>
    /// Apply only the approved subset of the latest turn's proposal onto the current draft (doc 14
    /// §4.1, granular merge). Stale <paramref name="expectedRevision"/> → <see cref="ProposalMergeOutcomeConflict"/>;
    /// a merged result that fails pure-tier validation → <see cref="ProposalMergeOutcomeValidationBlocked"/>;
    /// otherwise persists a new revision and returns <see cref="ProposalMergeOutcomeMerged"/>.
    /// </summary>
    Task<ProposalMergeOutcome> ApplyApprovedChangesAsync(
        string workflowId,
        IReadOnlyList<string> approvedChangeIds,
        string expectedRevision,
        string designerId,
        CancellationToken ct);
}

/// <summary>Result of merge attempt (discriminated union over outcome type).</summary>
public abstract record ProposalMergeOutcome
{
    public required string DraftRevisionAfterMerge { get; init; }
}

/// <summary>Proposal merged successfully (draft bumped revision).</summary>
public sealed record ProposalMergeOutcomeMerged : ProposalMergeOutcome
{
    public required DefinitionDraftDocument UpdatedDraft { get; init; }
}

/// <summary>Merge conflict: both designer and agent edited overlapping nodes.</summary>
public sealed record ProposalMergeOutcomeConflict : ProposalMergeOutcome
{
    public required IReadOnlyList<NodeConflict> Conflicts { get; init; }
    public required WorkflowDefinition DesignerEdit { get; init; }
    public required WorkflowDefinition AgentProposal { get; init; }
}

/// <summary>Validation failed: proposal violates rules (structure, refs, etc.).</summary>
public sealed record ProposalMergeOutcomeValidationBlocked : ProposalMergeOutcome
{
    public required IReadOnlyList<ValidationFinding> BlockingFindings { get; init; }
}

/// <summary>Single node conflict (designer and agent both modified the same node).</summary>
public sealed record NodeConflict
{
    public required string NodeId { get; init; }
    public required string DesignerVersion { get; init; } // Node JSON from draft at turn start
    public required string AgentProposedVersion { get; init; } // Node JSON from agent proposal
}
