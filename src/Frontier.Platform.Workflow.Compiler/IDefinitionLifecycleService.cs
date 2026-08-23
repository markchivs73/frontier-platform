using System.Diagnostics.CodeAnalysis;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Lifecycle operations for workflow definitions: drafts, publish proposals, approvals, retirement.
/// Doc 13 §2–§3: state machine enforcement, governance, versioning.
/// </summary>
public interface IDefinitionLifecycleService
{
    /// <summary>Create a new draft for a workflow, optionally copying an existing published version.</summary>
    Task<DraftHandle> CreateDraftAsync(string workflowId, int? fromVersion, string userId, CancellationToken ct);

    /// <summary>Save a draft with optimistic concurrency control (ETag).</summary>
    Task<SaveDraftResponse> SaveDraftAsync(
        string workflowId,
        WorkflowDefinition definition,
        string expectedRevision,
        string userId,
        CancellationToken ct);

    /// <summary>Apply an agent-proposed change set to the current draft.</summary>
    Task<MergeOutcome> ApplyAgentMergeAsync(
        string workflowId,
        IReadOnlyList<string> approvedChangeIds,
        string expectedRevision,
        string userId,
        CancellationToken ct);

    /// <summary>Propose publishing a draft.</summary>
    Task<PublishProposal> ProposePublishAsync(
        string workflowId,
        string draftRevision,
        ValidationReport report,
        string userId,
        CancellationToken ct);

    /// <summary>Approve a publish proposal.</summary>
    Task<PublishedVersion> ApproveAsync(
        string proposalId,
        string approverId,
        CancellationToken ct);

    /// <summary>Reject a publish proposal.</summary>
    Task RejectAsync(
        string proposalId,
        string approverId,
        string reason,
        CancellationToken ct);

    /// <summary>Retire a published version.</summary>
    Task RetireAsync(
        string workflowId,
        int version,
        string adminId,
        string reason,
        CancellationToken ct);

    /// <summary>Un-retire a version.</summary>
    Task UnretireAsync(
        string workflowId,
        int version,
        string adminId,
        string reason,
        CancellationToken ct);

    /// <summary>Get the full version history for a workflow.</summary>
    Task<VersionHistory> GetHistoryAsync(string workflowId, CancellationToken ct);

    /// <summary>Delete a draft not currently in review (doc 13 §2 delete transition, S9.42).</summary>
    Task<DeleteDraftResult> DeleteDraftAsync(string workflowId, CancellationToken ct);
}

// Response and contract types
[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
public sealed record DraftHandle(
    string WorkflowId,
    string DraftRevision,
    string ETag,
    DateTime CreatedUtc);

public abstract record SaveDraftResponse;

public sealed record SaveDraftResponseSuccess(DraftHandle Draft) : SaveDraftResponse;

public sealed record SaveDraftResponseConflict(string CurrentRevision, string CurrentETag, IReadOnlyList<NodeDiff> NodeDiff) : SaveDraftResponse;

[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
public sealed record NodeDiff(
    string Action,
    string NodeId,
    string? Origin);

public abstract record MergeOutcome;

public sealed record MergeOutcomeSuccess(DraftHandle Draft) : MergeOutcome;

public sealed record MergeOutcomeConflict(string CurrentRevision, string CurrentETag, IReadOnlyList<NodeDiff> NodeDiff) : MergeOutcome;

[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
public sealed record PublishProposal(
    string ProposalId,
    string WorkflowId,
    string DraftRevision,
    string ProposerId,
    DateTime ProposedAtUtc,
    ValidationReport ValidationReport,
    ProposalState State);

[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
public sealed record PublishedVersion(
    string WorkflowId,
    int DefinitionVersion,
    string DefinitionHash,
    string ProposedBy,
    string ApprovedBy,
    DateTime ApprovedAtUtc);

[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
public sealed record VersionHistory(
    string WorkflowId,
    DraftHandle? Draft,
    int CurrentVersion,
    IReadOnlyList<PublishedVersionInfo> Versions);

[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
public sealed record PublishedVersionInfo(
    int Version,
    string State,
    string DefinitionHash,
    DateTime PublishedAtUtc,
    string PublishedBy,
    string ApprovedBy,
    int? SupersededByVersion,
    RetirementInfo? RetirementInfo);

[ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
public sealed record RetirementInfo(
    DateTime RetiredAtUtc,
    string RetiredBy,
    string Reason);

/// <summary>S9.42: outcome of <see cref="IDefinitionLifecycleService.DeleteDraftAsync"/>.</summary>
public abstract record DeleteDraftResult;

/// <summary><paramref name="WorkflowDeleted"/> is true when the draft was the workflow's only, never-published version — C-30: no separate workflow catalogue document exists (doc 20/A1's listing is derived live from draft+version documents), so nothing further needs deleting; this flag is purely informational for the caller.</summary>
public sealed record DeleteDraftResultSuccess(bool WorkflowDeleted) : DeleteDraftResult;

public sealed record DeleteDraftResultNotFound : DeleteDraftResult;

/// <summary>The draft has a proposal currently `in_review` — doc 13 §2: withdraw it first (via an edit) rather than delete gaining a second withdrawal path.</summary>
public sealed record DeleteDraftResultInReview : DeleteDraftResult;
