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
    private readonly IDefinitionContractEmbedder _contracts;
    private readonly PublishGovernanceConfig _publishGovernance;

    public DefinitionLifecycleService(
        IDefinitionStore store,
        IDefinitionCompiler compiler,
        IDefinitionContractEmbedder contracts,
        PublishGovernanceConfig? publishGovernance = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(contracts);

        _store = store;
        _compiler = compiler;
        _contracts = contracts;
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

    /// <summary>Propose publishing a draft with no title or change summary.</summary>
    public Task<PublishProposal> ProposePublishAsync(
        string workflowId,
        string draftRevision,
        ValidationReport report,
        string userId,
        CancellationToken ct) =>
        ProposePublishAsync(workflowId, draftRevision, report, userId, title: null, changeSummary: null, ct);

    /// <inheritdoc />
    public async Task<PublishProposal> ProposePublishAsync(
        string workflowId,
        string draftRevision,
        ValidationReport report,
        string userId,
        string? title,
        string? changeSummary,
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
            ApproverNoteOrReason = null,
            Title = OptionalText(title),
            ChangeSummary = OptionalText(changeSummary)
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

    /// <inheritdoc />
    /// <remarks>
    /// Doc 13 §3, S13.248: the revision is validated again here, against the live registries, and
    /// refused with the findings when it no longer passes; the proposal, the version and the pointer
    /// are then written as one store operation. With a store that overrides it atomically
    /// (<see cref="CosmosDefinitionStore"/>), a failure part-way leaves the proposal in review and a
    /// re-run can approve it; the interface's default makes no such promise.
    /// </remarks>
    public async Task<PublishedVersion> ApproveAsync(
        string proposalId,
        string approverId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(proposalId)) throw new ArgumentException("Proposal ID required", nameof(proposalId));
        if (string.IsNullOrEmpty(approverId)) throw new ArgumentException("Approver ID required", nameof(approverId));

        var (proposal, draft) = await LoadApprovableAsync(proposalId, approverId, ct);
        await RevalidateAsync(proposal, draft, ct);

        // Pin the contracts before anything changes state, so a contract that cannot be pinned
        // leaves the proposal in review rather than approved with no version (ADR-E27 D15).
        var definition = await _contracts.EmbedAsync(draft.Definition, ct);
        var existingVersions = await _store.GetAllVersionsAsync(proposal.WorkflowId, ct);
        var pointer = await _store.GetCurrentVersionPointerAsync(proposal.WorkflowId, ct);
        var versionDoc = NewVersion(proposal, approverId, definition, NextVersionNumber(pointer, existingVersions));

        if (!await _store.ApproveAndPublishAsync(proposal, versionDoc, ct))
            throw new InvalidOperationException("Failed to approve proposal — it may have been modified concurrently");

        // GOV-14: the versions this one replaces become superseded. After the pointer moves, so a
        // failure here leaves the new version current and the old ones published, never the reverse.
        await SupersedePredecessorsAsync(proposal.WorkflowId, existingVersions, versionDoc.DefinitionVersion, ct);

        // The draft stays open for the next edit; publish does not retire it (Mark, 2026-11-05).
        return new PublishedVersion(
            WorkflowId: proposal.WorkflowId,
            DefinitionVersion: versionDoc.DefinitionVersion,
            DefinitionHash: versionDoc.DefinitionHash,
            ProposedBy: proposal.ProposerId,
            ApprovedBy: approverId,
            ApprovedAtUtc: versionDoc.ApprovedUtc);
    }

    /// <summary>
    /// The proposal and the draft it froze, once every guard that needs no validation has passed: the
    /// proposal exists and is in review, the distinct-approver policy allows <paramref name="approverId"/>,
    /// the draft is still at the proposed revision, and the report it was proposed with exists.
    /// </summary>
    private async Task<(PublishProposalDocument Proposal, DefinitionDraftDocument Draft)> LoadApprovableAsync(
        string proposalId,
        string approverId,
        CancellationToken ct)
    {
        var proposal = await _store.GetProposalAsync(proposalId, ct)
            ?? throw new InvalidOperationException($"Proposal {proposalId} not found");

        if (!proposal.State.CanTransitionTo(ProposalState.Approved))
            throw new InvalidOperationException($"Proposal is not in review (current state: {proposal.State})");

        if (_publishGovernance.RequireDistinctApprover && proposal.ProposerId == approverId)
            throw new InvalidOperationException("Distinct approver required: proposer cannot approve");

        var draft = await _store.GetDraftAsync(proposal.WorkflowId, ct)
            ?? throw new InvalidOperationException($"Draft not found for workflow {proposal.WorkflowId}");

        if (draft.DraftRevision != proposal.DraftRevision)
            throw new InvalidOperationException(
                "Draft has been edited since proposal — proposal auto-withdrawn. Please re-validate and re-propose.");

        _ = await _store.GetValidationReportAsync(proposal.WorkflowId, proposal.DraftRevision, ct)
            ?? throw new InvalidOperationException($"Validation report not found for revision {proposal.DraftRevision}");

        return (proposal, draft);
    }

    /// <summary>
    /// Runs the full rule catalogue (pure and resourced tiers) over the proposed revision again, so the
    /// resourced rules read today's registries rather than the ones the stored report saw: a tool retired
    /// or a server drifted since submission fails <c>mcp.tool-resolves</c> or <c>mcp.server-not-drifted</c>
    /// here (doc 13 §3). Refuses with the error findings; warnings do not block, as at proposal.
    /// </summary>
    private async Task RevalidateAsync(PublishProposalDocument proposal, DefinitionDraftDocument draft, CancellationToken ct)
    {
        var report = await _compiler.ValidateAsync(draft.Definition, draft.DraftRevision, draft.StoredSchemaVersion, ct);
        if (report.Outcome == ValidationOutcome.Fail)
            throw new PublishRevalidationException(proposal.Id, [.. report.Findings.Where(f => f.Severity == ValidationSeverity.Error)]);
    }

    /// <summary>
    /// The version document <paramref name="approverId"/>'s approval publishes, wrapping the
    /// definition <see cref="PublishedDefinition"/> stamps with its version number and hash.
    /// </summary>
    private DefinitionVersionDocument NewVersion(
        PublishProposalDocument proposal,
        string approverId,
        WorkflowDefinition draftDefinition,
        int version)
    {
        var definition = PublishedDefinition(draftDefinition, version);
        return new DefinitionVersionDocument
        {
            Id = $"{proposal.WorkflowId}:v{version}",
            WorkflowId = proposal.WorkflowId,
            State = "published",
            DefinitionVersion = version,
            DefinitionHash = definition.DefinitionHash,
            Definition = definition,
            ProposedBy = proposal.ProposerId,
            ApprovedBy = approverId,
            ProposedUtc = proposal.ProposedAtUtc,
            ApprovedUtc = DateTime.UtcNow,
            ValidationReportRef = proposal.ValidationReportRef.DocumentId,
            SupersededByVersion = null,
            Retirement = null,
            // S13.201: the version keeps the proposer's title and summary for its history view.
            Title = proposal.Title,
            ChangeSummary = proposal.ChangeSummary
        };
    }

    /// <summary>
    /// <paramref name="draftDefinition"/> as version <paramref name="version"/> publishes it: stamped
    /// with the version number first, so the hash covers the number a run records, then with that hash.
    /// </summary>
    private WorkflowDefinition PublishedDefinition(WorkflowDefinition draftDefinition, int version)
    {
        // S13.273: a draft carries 0 (new) or the version it was copied from, so runs built from the
        // inner definition recorded that, not the version they ran. Stored versions are never re-stamped.
        var definition = draftDefinition with { DefinitionVersion = version };
        // The definition a run carries inline (ADR-2) names its own hash, so the audit can say which
        // definition ran. Before S13.138 it kept the draft's empty field.
        return definition with { DefinitionHash = _compiler.ComputeDefinitionHash(definition) };
    }

    /// <summary>A proposer's optional text, or <see langword="null"/> when it is blank, so the key is omitted rather than stored empty.</summary>
    internal static string? OptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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

    /// <summary>
    /// Records <paramref name="successor"/> on each version <see cref="SupersessionTargets"/> names.
    /// A <see langword="false"/> from the store means the version changed state concurrently (for
    /// example an admin retired it), which leaves nothing to supersede.
    /// </summary>
    private async Task SupersedePredecessorsAsync(
        string workflowId,
        IReadOnlyList<DefinitionVersionDocument> existingVersions,
        int successor,
        CancellationToken ct)
    {
        foreach (var version in SupersessionTargets(existingVersions, successor))
            await _store.SupersedeVersionAsync(workflowId, version, successor, ct);
    }

    /// <summary>
    /// The versions a newly published <paramref name="successor"/> replaces: every older version that
    /// records no successor yet and is <c>published</c> or <c>retired</c>. A retired one stays retired
    /// but keeps the successor, so un-retiring it cannot leave two published versions (GOV-14). Every
    /// older version qualifies, not only the current one, so versions published before supersession
    /// existed are healed by the next publish.
    /// </summary>
    internal static IReadOnlyList<int> SupersessionTargets(
        IReadOnlyList<DefinitionVersionDocument> existingVersions,
        int successor) =>
        [.. existingVersions
            .Where(v => v.DefinitionVersion < successor
                && v.SupersededByVersion is null
                && v.State is "published" or "retired")
            .Select(v => v.DefinitionVersion)
            .Order()];

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

        var retirement = new RetirementInfoDocument
        {
            RetiredAtUtc = DateTime.UtcNow,
            RetiredBy = adminId,
            Reason = reason
        };

        // GOV-01: a dedicated state transition, never PublishVersionAsync — that path always writes
        // 'published' and returns an existing version unchanged, so the retirement was never stored.
        if (!await _store.RetireVersionAsync(workflowId, version, retirement, ct))
            throw new InvalidOperationException($"Failed to retire version {version} — it may have been modified concurrently");
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

        if (!await _store.UnretireVersionAsync(workflowId, version, ct))
            throw new InvalidOperationException($"Failed to un-retire version {version} — it may have been modified concurrently");
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
