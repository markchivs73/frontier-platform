namespace Frontier.Platform.Workflow.Compiler.Storage;

/// <summary>
/// Abstraction for persisting and retrieving workflow definitions across draft/published/proposed states.
/// Implements the storage schema per doc 13 §7.
/// </summary>
public interface IDefinitionStore
{
    /// <summary>Lists all workflows with their effective status for the A1 catalogue (doc 20).</summary>
    Task<WorkflowCataloguePage> ListWorkflowsAsync(
        string? engagementType,
        string? status,
        string? search,
        int offset,
        int limit,
        CancellationToken ct);

    // Draft operations
    Task<DefinitionDraftDocument> CreateDraftAsync(
        string workflowId,
        int baseVersion,
        DefinitionDraftDocument draft,
        CancellationToken ct);

    Task<DefinitionDraftDocument?> GetDraftAsync(
        string workflowId,
        CancellationToken ct);

    /// <summary>Conditional save: returns Success or Conflict with current revision/ETag.</summary>
    Task<SaveDraftResult> SaveDraftAsync(
        string workflowId,
        DefinitionDraftDocument draft,
        string expectedETag,
        CancellationToken ct);

    // Published version operations
    Task<DefinitionVersionDocument> PublishVersionAsync(
        DefinitionVersionDocument versionDoc,
        CancellationToken ct);

    Task<DefinitionVersionDocument?> GetVersionAsync(
        string workflowId,
        int version,
        CancellationToken ct);

    Task<IReadOnlyList<DefinitionVersionDocument>> GetAllVersionsAsync(
        string workflowId,
        CancellationToken ct);

    /// <summary>
    /// S9.55 (doc 13 ADR-DC5): every live (<c>published</c>, non-retired) version across all
    /// workflows — the daily re-validation sweep's work list. Cross-partition by design: a
    /// governance/sweep surface, same rationale as <see cref="ListPendingProposalsAsync"/>
    /// (cosmos-conventions).
    /// </summary>
    Task<IReadOnlyList<DefinitionVersionDocument>> ListPublishedVersionsAsync(
        CancellationToken ct);

    /// <summary>S9.55 (doc 13 ADR-DC5): upserts a version's health projection (sidecar <c>{workflowId}:v{n}:health</c>); idempotent — a re-run overwrites the prior sweep's result.</summary>
    Task<WorkflowHealthDocument> UpsertVersionHealthAsync(
        WorkflowHealthDocument health,
        CancellationToken ct);

    /// <summary>S9.55 (doc 13 ADR-DC5): every version-health projection for one workflow (partition-scoped) — backs the A2 versions endpoint's per-version health fields.</summary>
    Task<IReadOnlyList<WorkflowHealthDocument>> ListVersionHealthAsync(
        string workflowId,
        CancellationToken ct);

    /// <summary>
    /// S9.57: every version-health projection across all workflows — backs the A1 attention chip and
    /// the needs-attention dashboard worklist (each workflow's current-published health is its
    /// highest-version entry). Cross-partition read of a pre-computed projection, same governance
    /// convention as <see cref="ListAllWorkflowUsageAsync"/> (cosmos-conventions).
    /// </summary>
    Task<IReadOnlyList<WorkflowHealthDocument>> ListAllVersionHealthAsync(
        CancellationToken ct);

    /// <summary>S9.56 (C-34): upserts a workflow's usage/health rollup (sidecar <c>{workflowId}:usage</c>); idempotent — a re-run overwrites the prior sweep's rollup.</summary>
    Task<WorkflowUsageDocument> UpsertWorkflowUsageAsync(
        WorkflowUsageDocument usage,
        CancellationToken ct);

    /// <summary>
    /// S9.56 (C-34): every workflow usage rollup, for the A1 catalogue join. Cross-partition by
    /// design (one governance read of a pre-computed projection, never a per-request aggregate —
    /// cosmos-conventions), same rationale as <see cref="ListPendingProposalsAsync"/>.
    /// </summary>
    Task<IReadOnlyList<WorkflowUsageDocument>> ListAllWorkflowUsageAsync(
        CancellationToken ct);

    // Current version pointer
    Task<CurrentVersionPointerDocument?> GetCurrentVersionPointerAsync(
        string workflowId,
        CancellationToken ct);

    Task SetCurrentVersionAsync(
        string workflowId,
        int version,
        CancellationToken ct);

    // Proposal operations
    Task<PublishProposalDocument> CreateProposalAsync(
        PublishProposalDocument proposal,
        CancellationToken ct);

    Task<PublishProposalDocument?> GetProposalAsync(
        string proposalId,
        CancellationToken ct);

    /// <summary>Conditionally approve a proposal (fails if already in another state).</summary>
    Task<bool> ApproveProposalAsync(
        string proposalId,
        CancellationToken ct);

    /// <summary>Conditionally reject a proposal.</summary>
    Task<bool> RejectProposalAsync(
        string proposalId,
        string reason,
        CancellationToken ct);

    /// <summary>Withdraw a proposal (e.g., due to draft edit). Returns success if state was in_review.</summary>
    Task<bool> WithdrawProposalAsync(
        string proposalId,
        CancellationToken ct);

    /// <summary>S9.42: the workflow's currently in-review proposal, if any — used to guard draft deletion (doc 13 §2: delete is `draft`-state only, not `in_review`).</summary>
    Task<PublishProposalDocument?> GetActiveProposalAsync(
        string workflowId,
        CancellationToken ct);

    /// <summary>
    /// S9.48 (doc 19 C1, doc 20 <c>GET /api/approvals/mine</c>): every in-review proposal across
    /// all workflows, newest first — the approvals inbox's publish-proposals group. Cross-partition
    /// by design: a governance/inbox surface, same rationale as the dead-letter list (cosmos-conventions).
    /// </summary>
    Task<IReadOnlyList<PublishProposalDocument>> ListPendingProposalsAsync(
        CancellationToken ct);

    /// <summary>
    /// S9.42 (doc 13 §2 delete transition): deletes the draft's own artifacts — the draft
    /// document, its chat history, all chat turns, and all test-run documents. Does not touch
    /// published/superseded/retired version documents, the current-version pointer, or proposal
    /// history — those persist independently of any single draft revision.
    /// </summary>
    Task DeleteDraftAsync(
        string workflowId,
        CancellationToken ct);

    // Validation report persistence
    Task<ValidationReportDocument> PersistValidationReportAsync(
        ValidationReportDocument report,
        CancellationToken ct);

    Task<ValidationReportDocument?> GetValidationReportAsync(
        string workflowId,
        string draftRevision,
        CancellationToken ct);

    // Test-run operations (ephemeral, advisory evidence)
    Task<TestRunDocument> PersistTestRunAsync(
        TestRunDocument testRun,
        CancellationToken ct);

    Task<TestRunDocument?> GetTestRunAsync(
        string testRunId,
        CancellationToken ct);

    /// <summary>S9.38d: every test-run document for <paramref name="workflowId"/>, most recent first (A4 "Prior runs" table).</summary>
    Task<IReadOnlyList<TestRunDocument>> ListTestRunsAsync(
        string workflowId,
        CancellationToken ct);

    /// <summary>
    /// S9.86: every non-terminal test-run document (`status` <c>running</c> or <c>paused_at_gate</c>)
    /// across all workflows, most recent first — the S9.86 finalizer sweep's work list and the S9.88
    /// active-runs rollup's source. Cross-partition, but narrow by construction: only the handful of
    /// currently-active sandbox runs match (the S9.57 <c>ListAllVersionHealthAsync</c> precedent);
    /// legacy documents (pre-S9.85, no status) never match — they are terminal by definition.
    /// </summary>
    Task<IReadOnlyList<TestRunDocument>> ListActiveTestRunsAsync(
        CancellationToken ct);

    // Chat designer operations
    Task<DesignTurnDocument> PersistDesignTurnAsync(
        DesignTurnDocument turn,
        CancellationToken ct);

    Task<DesignTurnDocument?> GetDesignTurnAsync(
        string turnDocumentId,
        CancellationToken ct);

    Task<ChatHistoryDocument?> GetChatHistoryAsync(
        string workflowId,
        CancellationToken ct);

    Task<ChatHistoryDocument> CreateOrUpdateChatHistoryAsync(
        ChatHistoryDocument history,
        CancellationToken ct);

    Task<IReadOnlyList<DesignTurnDocument>> GetAllDesignTurnsAsync(
        string workflowId,
        CancellationToken ct);
}

/// <summary>Result of a conditional save operation.</summary>
public abstract record SaveDraftResult;

public sealed record SaveDraftResultSuccess(DefinitionDraftDocument Draft) : SaveDraftResult;

public sealed record SaveDraftResultConflict(
    string CurrentETag,
    string CurrentRevision,
    DefinitionDraftDocument CurrentDocument) : SaveDraftResult;
