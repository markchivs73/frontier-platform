using System.Text;

using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Orchestrates the definition lifecycle: draft editing, validation, publish proposals, approvals, retirement.
/// Doc 13 §2–3: state machine transitions, governance, concurrency control.
/// Phase 1B implementation: core state machine + Cosmos storage + ETag concurrency.
/// </summary>
public sealed class DefinitionLifecycleService : IDefinitionLifecycleService
{
    private readonly IDefinitionStore _store;
    private readonly IDefinitionCompiler _compiler;
    private readonly PublishGovernanceConfig _publishGovernance;

    public DefinitionLifecycleService(
        IDefinitionStore store,
        IDefinitionCompiler compiler,
        PublishGovernanceConfig? publishGovernance = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);

        _store = store;
        _compiler = compiler;
        _publishGovernance = publishGovernance ?? new PublishGovernanceConfig();
    }

    public async Task<DraftHandle> CreateDraftAsync(
        string workflowId,
        int? fromVersion,
        string userId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID required", nameof(workflowId));
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("User ID required", nameof(userId));

        // Check if draft already exists
        var existingDraft = await _store.GetDraftAsync(workflowId, ct);
        if (existingDraft != null)
            throw new InvalidOperationException($"Draft already exists for workflow {workflowId}");

        int baseVersion = 0;
        WorkflowDefinition definition;

        if (fromVersion.HasValue)
        {
            var sourceVersion = await _store.GetVersionAsync(workflowId, fromVersion.Value, ct);
            if (sourceVersion == null)
                throw new InvalidOperationException($"Source version {fromVersion} not found");
            definition = sourceVersion.Definition;
            baseVersion = fromVersion.Value;
        }
        else
        {
            // Brand-new workflow: start with an empty definition at version 0.
            // The client populates name/engagement-type/nodes via the first SaveDraftAsync call.
            definition = new WorkflowDefinition
            {
                WorkflowId = workflowId,
                DefinitionVersion = 0,
                EngagementType = string.Empty,
                Name = string.Empty,
                Nodes = [],
                Edges = [],
                DefinitionHash = string.Empty,
                Mode = ExecutionMode.OneShot
            };
        }

        var draftRevision = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var draft = new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = baseVersion,
            DraftRevision = draftRevision,
            Definition = definition,
            LastEditedBy = userId,
            LastEditedUtc = now
        };

        var stored = await _store.CreateDraftAsync(workflowId, baseVersion, draft, ct);

        return new DraftHandle(
            WorkflowId: workflowId,
            DraftRevision: stored.DraftRevision,
            ETag: draftRevision,
            CreatedUtc: now);
    }

    public async Task<SaveDraftResponse> SaveDraftAsync(
        string workflowId,
        WorkflowDefinition definition,
        string expectedRevision,
        string userId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID required", nameof(workflowId));
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrEmpty(expectedRevision)) throw new ArgumentException("Expected revision required", nameof(expectedRevision));
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("User ID required", nameof(userId));

        var current = await _store.GetDraftAsync(workflowId, ct);
        if (current == null)
            throw new InvalidOperationException($"Draft not found for workflow {workflowId}");

        var newRevision = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var updated = new DefinitionDraftDocument
        {
            Id = current.Id,
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = current.BaseVersion,
            DraftRevision = newRevision,
            Definition = definition,
            LastEditedBy = userId,
            LastEditedUtc = now
        };

        var saveResult = await _store.SaveDraftAsync(workflowId, updated, expectedRevision, ct);

        return saveResult switch
        {
            SaveDraftResultSuccess success => new SaveDraftResponseSuccess(
                new DraftHandle(
                    WorkflowId: workflowId,
                    DraftRevision: success.Draft.DraftRevision,
                    ETag: newRevision,
                    CreatedUtc: now)),

            SaveDraftResultConflict conflict => new SaveDraftResponseConflict(
                CurrentRevision: conflict.CurrentRevision,
                CurrentETag: conflict.CurrentETag,
                NodeDiff: []),  // Phase 1: node diff summary deferred

            _ => throw new InvalidOperationException("Unknown save result")
        };
    }

    public async Task<MergeOutcome> ApplyAgentMergeAsync(
        string workflowId,
        IReadOnlyList<string> approvedChangeIds,
        string expectedRevision,
        string userId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID required", nameof(workflowId));
        ArgumentNullException.ThrowIfNull(approvedChangeIds);
        if (string.IsNullOrEmpty(expectedRevision)) throw new ArgumentException("Expected revision required", nameof(expectedRevision));
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("User ID required", nameof(userId));

        // Phase 1: agent merge deferred — stub returns conflict on mismatch
        var current = await _store.GetDraftAsync(workflowId, ct);
        if (current == null)
            throw new InvalidOperationException($"Draft not found for workflow {workflowId}");

        if (current.DraftRevision != expectedRevision)
        {
            return new MergeOutcomeConflict(
                CurrentRevision: current.DraftRevision,
                CurrentETag: expectedRevision,
                NodeDiff: []);
        }

        // Stub: no actual merge logic
        return new MergeOutcomeSuccess(
            new DraftHandle(
                WorkflowId: workflowId,
                DraftRevision: current.DraftRevision,
                ETag: expectedRevision,
                CreatedUtc: current.LastEditedUtc));
    }

    public async Task<PublishProposal> ProposePublishAsync(
        string workflowId,
        string draftRevision,
        ValidationReport report,
        string userId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID required", nameof(workflowId));
        if (string.IsNullOrEmpty(draftRevision)) throw new ArgumentException("Draft revision required", nameof(draftRevision));
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("User ID required", nameof(userId));

        // Verify draft exists and matches the revision
        var draft = await _store.GetDraftAsync(workflowId, ct);
        if (draft == null)
            throw new InvalidOperationException($"Draft not found for workflow {workflowId}");

        if (draft.DraftRevision != draftRevision)
            throw new InvalidOperationException($"Draft revision mismatch: expected {draftRevision}, got {draft.DraftRevision}");

        // Validate that the report passes
        if (report.Outcome == ValidationOutcome.Fail)
            throw new InvalidOperationException("Cannot propose publish with failing validation");

        // Persist the validation report
        var reportDoc = new ValidationReportDocument
        {
            Id = $"{workflowId}:report:{draftRevision}",
            WorkflowId = workflowId,
            DraftRevision = draftRevision,
            ValidatedAtUtc = report.ValidatedAtUtc,
            Outcome = report.Outcome,
            Findings = report.Findings,
            ResourceVersions = report.ResourceVersions
        };

        await _store.PersistValidationReportAsync(reportDoc, ct);

        // Create proposal
        var proposalId = $"{workflowId}:proposal:{Guid.NewGuid()}";
        var now = DateTime.UtcNow;

        var proposal = new PublishProposalDocument
        {
            Id = proposalId,
            WorkflowId = workflowId,
            DraftRevision = draftRevision,
            ProposerId = userId,
            ProposedAtUtc = now,
            ValidationReportRef = new ValidationReportRef { DocumentId = reportDoc.Id },
            State = ProposalState.InReview,
            ApproverNoteOrReason = null
        };

        var storedProposal = await _store.CreateProposalAsync(proposal, ct);

        return new PublishProposal(
            ProposalId: storedProposal.Id,
            WorkflowId: storedProposal.WorkflowId,
            DraftRevision: storedProposal.DraftRevision,
            ProposerId: storedProposal.ProposerId,
            ProposedAtUtc: storedProposal.ProposedAtUtc,
            ValidationReport: report,
            State: storedProposal.State);
    }

    public async Task<PublishedVersion> ApproveAsync(
        string proposalId,
        string approverId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(proposalId)) throw new ArgumentException("Proposal ID required", nameof(proposalId));
        if (string.IsNullOrEmpty(approverId)) throw new ArgumentException("Approver ID required", nameof(approverId));

        var proposal = await _store.GetProposalAsync(proposalId, ct);
        if (proposal == null)
            throw new InvalidOperationException($"Proposal {proposalId} not found");

        if (!proposal.State.CanTransitionTo(ProposalState.Approved))
            throw new InvalidOperationException($"Proposal is not in review (current state: {proposal.State})");

        // Distinct-approver policy check
        if (_publishGovernance.RequireDistinctApprover && proposal.ProposerId == approverId)
            throw new InvalidOperationException("Distinct approver required: proposer cannot approve");

        // Re-validate draft (pure tier only) and check resource drift
        var draft = await _store.GetDraftAsync(proposal.WorkflowId, ct);
        if (draft == null)
            throw new InvalidOperationException($"Draft not found for workflow {proposal.WorkflowId}");

        if (draft.DraftRevision != proposal.DraftRevision)
            throw new InvalidOperationException(
                "Draft has been edited since proposal — proposal auto-withdrawn. Please re-validate and re-propose.");

        // Get the validation report
        var report = await _store.GetValidationReportAsync(proposal.WorkflowId, proposal.DraftRevision, ct);
        if (report == null)
            throw new InvalidOperationException($"Validation report not found for revision {proposal.DraftRevision}");

        // Check resourceVersions for drift (Phase 1: log drift, don't block)
        // Full implementation with registry version checks deferred to Phase B hardening

        // Mark proposal as approved
        var approved = await _store.ApproveProposalAsync(proposalId, ct);
        if (!approved)
            throw new InvalidOperationException("Failed to approve proposal — it may have been modified concurrently");

        // Compute next version number. Base it on the highest version that has ever existed —
        // not the current-version pointer alone — so a pointer that lags the true max (after a
        // retire/unretire, or an environment re-seeded with a rolled-back pointer) can never mint
        // a number that collides with an existing version document, which PublishVersionAsync
        // rejects on hash mismatch.
        var pointer = await _store.GetCurrentVersionPointerAsync(proposal.WorkflowId, ct);
        var existingVersions = await _store.GetAllVersionsAsync(proposal.WorkflowId, ct);
        int nextVersion = NextVersionNumber(pointer, existingVersions);

        // Create published version
        var definitionHash = _compiler.ComputeDefinitionHash(draft.Definition);
        var now = DateTime.UtcNow;

        var versionDoc = new DefinitionVersionDocument
        {
            Id = $"{proposal.WorkflowId}:v{nextVersion}",
            WorkflowId = proposal.WorkflowId,
            State = "published",
            DefinitionVersion = nextVersion,
            DefinitionHash = definitionHash,
            Definition = draft.Definition,
            ProposedBy = proposal.ProposerId,
            ApprovedBy = approverId,
            ProposedUtc = proposal.ProposedAtUtc,
            ApprovedUtc = now,
            ValidationReportRef = proposal.ValidationReportRef.DocumentId,
            SupersededByVersion = null,
            Retirement = null
        };

        await _store.PublishVersionAsync(versionDoc, ct);

        // Update current pointer
        await _store.SetCurrentVersionAsync(proposal.WorkflowId, nextVersion, ct);

        // Delete draft (it's superseded)
        // Phase 1: keep draft visible in UI; full cleanup deferred

        return new PublishedVersion(
            WorkflowId: proposal.WorkflowId,
            DefinitionVersion: nextVersion,
            DefinitionHash: definitionHash,
            ProposedBy: proposal.ProposerId,
            ApprovedBy: approverId,
            ApprovedAtUtc: now);
    }

    /// <summary>
    /// The next version number: one past whichever is greater of the current-version pointer and
    /// the highest version document that already exists (published/superseded/retired). Basing it
    /// on the true max — not the pointer alone — keeps version numbers unique and monotonic even
    /// when the pointer lags the highest version (retire/unretire, re-seeded environment).
    /// </summary>
    internal static int NextVersionNumber(
        CurrentVersionPointerDocument? pointer,
        IReadOnlyList<DefinitionVersionDocument> existingVersions)
    {
        int highestExisting = existingVersions.Count > 0 ? existingVersions.Max(v => v.DefinitionVersion) : 0;
        int pointerVersion = pointer?.CurrentVersion ?? 0;
        return Math.Max(pointerVersion, highestExisting) + 1;
    }

    public async Task RejectAsync(
        string proposalId,
        string approverId,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(proposalId)) throw new ArgumentException("Proposal ID required", nameof(proposalId));
        if (string.IsNullOrEmpty(approverId)) throw new ArgumentException("Approver ID required", nameof(approverId));
        if (string.IsNullOrEmpty(reason)) throw new ArgumentException("Reason required", nameof(reason));

        var proposal = await _store.GetProposalAsync(proposalId, ct);
        if (proposal == null)
            throw new InvalidOperationException($"Proposal {proposalId} not found");

        if (!proposal.State.CanTransitionTo(ProposalState.Rejected))
            throw new InvalidOperationException($"Proposal is not in review (current state: {proposal.State})");

        var rejected = await _store.RejectProposalAsync(proposalId, reason, ct);
        if (!rejected)
            throw new InvalidOperationException("Failed to reject proposal — it may have been modified concurrently");
    }

    public async Task RetireAsync(
        string workflowId,
        int version,
        string adminId,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID required", nameof(workflowId));
        if (string.IsNullOrEmpty(adminId)) throw new ArgumentException("Admin ID required", nameof(adminId));
        if (string.IsNullOrEmpty(reason)) throw new ArgumentException("Reason required", nameof(reason));

        var versionDoc = await _store.GetVersionAsync(workflowId, version, ct);
        if (versionDoc == null)
            throw new InvalidOperationException($"Version {version} not found");

        if (versionDoc.State == "retired")
            throw new InvalidOperationException($"Version {version} is already retired");

        var retired = versionDoc with
        {
            State = "retired",
            Retirement = new RetirementInfoDocument
            {
                RetiredAtUtc = DateTime.UtcNow,
                RetiredBy = adminId,
                Reason = reason
            }
        };

        // Phase 1: Upsert to mark retired — full Cosmos patch deferred
        // For now, we re-store the document with updated state
        // Production would use PATCH to minimize round-trip
        await _store.PublishVersionAsync(retired, ct);
    }

    public async Task UnretireAsync(
        string workflowId,
        int version,
        string adminId,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID required", nameof(workflowId));
        if (string.IsNullOrEmpty(adminId)) throw new ArgumentException("Admin ID required", nameof(adminId));
        if (string.IsNullOrEmpty(reason)) throw new ArgumentException("Reason required", nameof(reason));

        var versionDoc = await _store.GetVersionAsync(workflowId, version, ct);
        if (versionDoc == null)
            throw new InvalidOperationException($"Version {version} not found");

        if (versionDoc.State != "retired")
            throw new InvalidOperationException($"Version {version} is not retired");

        var unretired = versionDoc with
        {
            State = "published",
            Retirement = null
        };

        await _store.PublishVersionAsync(unretired, ct);
    }

    public async Task<VersionHistory> GetHistoryAsync(
        string workflowId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID required", nameof(workflowId));

        var draft = await _store.GetDraftAsync(workflowId, ct);
        var pointer = await _store.GetCurrentVersionPointerAsync(workflowId, ct);
        var versions = await _store.GetAllVersionsAsync(workflowId, ct);

        var versionInfos = versions.Select(v => new PublishedVersionInfo(
            Version: v.DefinitionVersion,
            State: v.State,
            DefinitionHash: v.DefinitionHash,
            PublishedAtUtc: v.ApprovedUtc,
            PublishedBy: v.ProposedBy,
            ApprovedBy: v.ApprovedBy,
            SupersededByVersion: v.SupersededByVersion,
            RetirementInfo: v.Retirement != null
                ? new RetirementInfo(
                    RetiredAtUtc: v.Retirement.RetiredAtUtc,
                    RetiredBy: v.Retirement.RetiredBy,
                    Reason: v.Retirement.Reason)
                : null)).ToList();

        var draftHandle = draft != null
            ? new DraftHandle(
                WorkflowId: draft.WorkflowId,
                DraftRevision: draft.DraftRevision,
                ETag: draft.DraftRevision,
                CreatedUtc: draft.LastEditedUtc)
            : null;

        return new VersionHistory(
            WorkflowId: workflowId,
            Draft: draftHandle,
            CurrentVersion: pointer?.CurrentVersion ?? 0,
            Versions: versionInfos.AsReadOnly());
    }

    public async Task<DeleteDraftResult> DeleteDraftAsync(
        string workflowId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workflowId)) throw new ArgumentException("Workflow ID required", nameof(workflowId));

        var draft = await _store.GetDraftAsync(workflowId, ct);
        if (draft is null) return new DeleteDraftResultNotFound();

        var activeProposal = await _store.GetActiveProposalAsync(workflowId, ct);
        if (activeProposal is not null) return new DeleteDraftResultInReview();

        var versions = await _store.GetAllVersionsAsync(workflowId, ct);
        var workflowDeleted = versions.Count == 0;

        await _store.DeleteDraftAsync(workflowId, ct);

        return new DeleteDraftResultSuccess(workflowDeleted);
    }
}

/// <summary>Configuration for publish governance (role-based approval).</summary>
public sealed record PublishGovernanceConfig(bool RequireDistinctApprover = false);
