using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IMappingGovernanceService"/> (doc 08 §7-8, ADR-PA32). Every decision is audited before
/// it is attempted and compensated if it does not land; every state move is guarded by the proposal's
/// ETag; and every mapping version is allocated by a create-only write on <c>{roleId}:v{n}</c>, so
/// concurrent approvals serialise rather than collide.
/// </summary>
internal sealed class MappingGovernanceService(
    IMappingProposalStore store,
    IRoleMappingWriter writer,
    IMappingDecisionRecorder recorder,
    TimeProvider timeProvider,
    IOptions<MappingGovernanceOptions> options) : IMappingGovernanceService
{

    /// <inheritdoc />
    public async Task<MappingChangeProposal> ProposeAsync(MappingChange change, string proposedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        MappingProposalRules.EnsureAttributed(proposedBy, change.Reason);
        MappingProposalRules.ValidateChange(change);
        await EnsureNoUndecidedProposalAsync(change.RoleId, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var proposal = BuildProposal(change, proposedBy, now, await FindFleetVersionAsync(change.RoleId, cancellationToken));
        proposal.Validate();

        var decision = Decision(MappingGovernanceEventTypes.Proposed, proposal, proposedBy, change.Reason, now);
        await RecordThenAsync(decision, () => store.CreateProposalAsync(proposal, cancellationToken), cancellationToken);

        return proposal;
    }

    /// <inheritdoc />
    public Task<MappingChangeProposal> ApproveAsync(
        string roleId, string proposalId, string approverId, string reason, string? expectedConcurrencyToken, CancellationToken cancellationToken) =>
        AllocateAsync(roleId, proposalId, approverId, reason, MappingProposalState.Approved, expectedConcurrencyToken, cancellationToken);

    /// <inheritdoc />
    public Task<MappingChangeProposal> PromoteAsync(
        string roleId, string proposalId, string actor, string reason, string? expectedConcurrencyToken, CancellationToken cancellationToken) =>
        AllocateAsync(roleId, proposalId, actor, reason, MappingProposalState.Promoted, expectedConcurrencyToken, cancellationToken);

    /// <inheritdoc />
    public Task<MappingChangeProposal> RejectAsync(
        string roleId, string proposalId, string approverId, string reason, string? expectedConcurrencyToken, CancellationToken cancellationToken) =>
        DecideAsync(roleId, proposalId, approverId, reason, MappingProposalState.Rejected, MappingGovernanceEventTypes.Rejected, expectedConcurrencyToken, cancellationToken);

    /// <inheritdoc />
    public Task<MappingChangeProposal> WithdrawAsync(
        string roleId, string proposalId, string actor, string reason, string? expectedConcurrencyToken, CancellationToken cancellationToken) =>
        DecideAsync(roleId, proposalId, actor, reason, MappingProposalState.Withdrawn, MappingGovernanceEventTypes.Withdrawn, expectedConcurrencyToken, cancellationToken);

    /// <inheritdoc />
    public async Task<MappingChangeProposal?> GetProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);

        var stored = await store.FindProposalAsync(roleId, proposalId, cancellationToken);
        return stored is null ? null : stored.Proposal with { ConcurrencyToken = stored.ETag };
    }

    /// <inheritdoc />
    public Task<MappingProposalPage> ListProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        // No role is legitimate (ADR-PA34): it is D3's cross-role "everything awaiting a decision" view.
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, MappingProposalQuery.MaxPageSize, nameof(query));

        return store.QueryProposalsAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MappingRollbackResult> RollbackToVersionAsync(string roleId, int toVersion, string actor, string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        MappingProposalRules.EnsureAttributed(actor, reason);

        var target = await EnsureRollbackTargetAsync(roleId, toVersion, cancellationToken);
        var previous = await store.FindCurrentVersionAsync(roleId, cancellationToken)
            ?? throw new UnknownMappingVersionException(nameof(IMappingGovernanceService), [$"role '{roleId}' has no current mapping pointer to roll back."]);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var decision = RollbackDecision(roleId, actor, reason, now, previous, toVersion, target.Ring);
        await RecordThenAsync(decision, () => writer.WriteCurrentAsync(roleId, toVersion, cancellationToken), cancellationToken);
        await MarkRolledBackAsync(roleId, previous, actor, reason, now, cancellationToken);

        return new MappingRollbackResult
        {
            RoleId = roleId,
            PreviousVersion = previous,
            CurrentVersion = toVersion,
            EffectiveAtUtc = now,
            Ring = target.Ring,
        };
    }

    /// <summary>
    /// A role holds one proposal at a time (Mark's call, 2026-09-16). This is a governance rule rather
    /// than a storage constraint: an approver should never have to work out which of several competing
    /// proposals wins, and approving two in sequence must not quietly supersede the first rollout. The
    /// refusal names the proposal in the way and who raised it, so the caller can go and review it.
    /// Refusals happen before the audit record, so a refused propose writes nothing.
    /// </summary>
    internal async Task EnsureNoUndecidedProposalAsync(string roleId, CancellationToken cancellationToken)
    {
        if (await store.FindUndecidedProposalAsync(roleId, cancellationToken) is not { } existing)
        {
            return;
        }

        throw new MappingProposalAlreadyPendingException(
            nameof(IMappingGovernanceService),
            [$"role '{roleId}' already has proposal '{existing.Proposal.ProposalId}' awaiting a decision, raised by "
             + $"'{existing.Proposal.ProposedBy}'; decide that proposal before raising another."]);
    }

    /// <summary>
    /// The rollback target must exist and must never be a shadow version: a shadow mapping was never
    /// served and making it current would put an unserved chain in front of every new execution — and
    /// would trip <see cref="RoleCatalogueCheck"/> at the next boot.
    /// </summary>
    internal async Task<RoleMapping> EnsureRollbackTargetAsync(string roleId, int toVersion, CancellationToken cancellationToken)
    {
        var target = await store.FindMappingVersionAsync(roleId, toVersion, cancellationToken)
            ?? throw new UnknownMappingVersionException(
                nameof(IMappingGovernanceService), [$"role '{roleId}' has no mapping version {toVersion} to roll back to."]);

        return target.Ring == RolloutRing.Shadow
            ? throw new MappingVersionNotRollbackEligibleException(
                nameof(IMappingGovernanceService),
                [$"role '{roleId}' mapping v{toVersion} is a shadow version and has never served; it cannot become current."])
            : target;
    }

    /// <summary>Moves the proposal that owned the version just rolled away from into its terminal state, when there is one.</summary>
    internal async Task MarkRolledBackAsync(string roleId, int previousVersion, string actor, string reason, DateTime now, CancellationToken cancellationToken)
    {
        // A point query on the version, not a page scan: a role with more proposals than one page
        // would otherwise have silently kept its owning proposal open after a rollback.
        if (await store.FindProposalByLiveVersionAsync(roleId, previousVersion, cancellationToken) is not { } stored
            || !stored.Proposal.State.CanTransitionTo(MappingProposalState.RolledBack))
        {
            return;
        }

        var updated = stored.Proposal with
        {
            State = MappingProposalState.RolledBack,
            RolledBackToVersion = previousVersion,
            DecidedBy = actor,
            DecidedAtUtc = now,
            DecisionReason = reason,
        };

        await store.TryReplaceProposalAsync(updated, stored.ETag, cancellationToken);
    }

    /// <summary>The version a proposal put in front of new executions: its fleet version if promoted, else its canary one.</summary>
    internal static int? LiveVersionOf(MappingChangeProposal proposal) =>
        proposal.PromotedVersion ?? proposal.MappingVersion;

    /// <summary>
    /// Approve and promote share one shape: read the proposal, compute the next version, audit the
    /// intended change, then land it in one conditional batch — retrying with a fresh version and a
    /// fresh record whenever another writer got there first.
    /// </summary>
    internal async Task<MappingChangeProposal> AllocateAsync(
        string roleId, string proposalId, string actor, string reason, MappingProposalState target, string? expectedToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        MappingProposalRules.EnsureAttributed(actor, reason);

        var retry = options.Value;
        for (var attempt = 1; attempt <= retry.DecisionMaxAttempts; attempt++)
        {
            var stored = await LoadForTransitionAsync(roleId, proposalId, target, expectedToken, cancellationToken);
            MappingProposalRules.EnsureDistinctApprover(stored.Proposal, actor);

            if (await TryAllocateOnceAsync(stored, actor, reason, target, cancellationToken) is { } updated)
            {
                return updated;
            }

            await DelayBeforeRetryAsync(attempt, retry, cancellationToken);
        }

        throw new MappingGovernanceException(
            $"The decision on proposal '{proposalId}' for role '{roleId}' kept losing its mapping-version allocation to concurrent writers; the {retry.DecisionMaxAttempts} attempts are exhausted.");
    }

    /// <summary>One allocate-audit-write pass: the updated proposal, or <see langword="null"/> when another writer won.</summary>
    internal async Task<MappingChangeProposal?> TryAllocateOnceAsync(
        StoredMappingProposal stored, string actor, string reason, MappingProposalState target, CancellationToken cancellationToken)
    {
        var proposal = stored.Proposal;
        var versions = await store.ListMappingVersionsAsync(proposal.RoleId, cancellationToken);
        var next = versions.Count == 0 ? 1 : versions.Max() + 1;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var mapping = BuildMapping(proposal, next, actor, reason, now, target);
        var updated = ApplyDecision(proposal, mapping, actor, reason, now, target);
        var decision = Decision(EventFor(target), updated, actor, reason, now) with
        {
            FromVersion = LiveVersionOf(proposal),
            ToVersion = next,
            Ring = mapping.Ring,
        };

        var recordId = await recorder.RecordAsync(decision, cancellationToken);
        var token = await TryLandAsync(mapping, updated, stored.ETag, decision, recordId, cancellationToken);

        return token is null ? null : updated with { ConcurrencyToken = token };
    }

    /// <summary>Runs the conditional batch, compensating the audit record when it does not land.</summary>
    internal async Task<string?> TryLandAsync(
        RoleMapping mapping, MappingChangeProposal updated, string etag, MappingGovernanceDecision decision, string recordId, CancellationToken cancellationToken)
    {
        string? landed;
        try
        {
            // A shadow version is stored but never pointed at: it has not served and must not serve.
            landed = await store.TryAllocateVersionAsync(mapping, updated, etag, mapping.Ring != RolloutRing.Shadow, cancellationToken);
        }
        catch (Exception ex)
        {
            await recorder.RecordCompensationAsync(decision, recordId, ex.Message, CancellationToken.None);
            throw;
        }

        if (landed is null)
        {
            await recorder.RecordCompensationAsync(decision, recordId, "another writer allocated this mapping version first; the decision was retried.", CancellationToken.None);
        }

        return landed;
    }

    /// <summary>Reject and withdraw share one shape: no version, one ETag-guarded replace.</summary>
    internal async Task<MappingChangeProposal> DecideAsync(
        string roleId, string proposalId, string actor, string reason, MappingProposalState target, string eventType, string? expectedToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        MappingProposalRules.EnsureAttributed(actor, reason);

        var retry = options.Value;
        for (var attempt = 1; attempt <= retry.DecisionMaxAttempts; attempt++)
        {
            var stored = await LoadForTransitionAsync(roleId, proposalId, target, expectedToken, cancellationToken);
            EnsureDecisionAuthority(stored.Proposal, actor, target);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var updated = stored.Proposal with { State = target, DecidedBy = actor, DecidedAtUtc = now, DecisionReason = reason };
            var decision = Decision(eventType, updated, actor, reason, now);

            var recordId = await recorder.RecordAsync(decision, cancellationToken);
            if (await store.TryReplaceProposalAsync(updated, stored.ETag, cancellationToken) is { } token)
            {
                return updated with { ConcurrencyToken = token };
            }

            await recorder.RecordCompensationAsync(decision, recordId, "another decision reached this proposal first; the decision was retried.", CancellationToken.None);
            await DelayBeforeRetryAsync(attempt, retry, cancellationToken);
        }

        throw new MappingGovernanceException($"The decision on proposal '{proposalId}' for role '{roleId}' kept losing to concurrent writers; the {retry.DecisionMaxAttempts} attempts are exhausted.");
    }

    /// <summary>
    /// Who may take this decision (Mark's call, 2026-09-16): only the proposer may <b>withdraw</b>, and
    /// only someone other than the proposer may <b>reject</b> — the same distinct-approver rule
    /// approval already carries, because a rejection is a recorded decision on someone else's change.
    /// </summary>
    internal static void EnsureDecisionAuthority(MappingChangeProposal proposal, string actor, MappingProposalState target)
    {
        if (target == MappingProposalState.Withdrawn)
        {
            MappingProposalRules.EnsureProposerOnly(proposal, actor);
        }
        else
        {
            MappingProposalRules.EnsureDistinctApprover(proposal, actor);
        }
    }

    /// <summary>Spreads contending decisions apart before the next attempt; no delay after the last one.</summary>
    internal static Task DelayBeforeRetryAsync(int attempt, MappingGovernanceOptions retry, CancellationToken cancellationToken) =>
        attempt < retry.DecisionMaxAttempts
            ? Task.Delay(MappingGovernanceBackoff.DelayFor(attempt, retry, MappingGovernanceBackoff.NextJitter()), cancellationToken)
            : Task.CompletedTask;

    /// <summary>
    /// Reads a proposal and refuses the decision unless it may legally be taken: the caller's view is
    /// current (when it supplied a token), and the move is legal from where the proposal is.
    /// The token is checked <b>first</b> — a caller looking at a stale proposal is reasoning about a
    /// state that no longer exists, so every later judgement it made is suspect.
    /// </summary>
    internal async Task<StoredMappingProposal> LoadForTransitionAsync(
        string roleId, string proposalId, MappingProposalState target, string? expectedToken, CancellationToken cancellationToken)
    {
        var stored = await store.FindProposalAsync(roleId, proposalId, cancellationToken)
            ?? throw new UnknownMappingProposalException(nameof(IMappingGovernanceService), [$"role '{roleId}' has no proposal '{proposalId}'."]);

        EnsureExpectedToken(stored, expectedToken);
        MappingProposalRules.EnsureTransition(stored.Proposal, target);
        return stored;
    }

    /// <summary>
    /// Refuses the decision when the caller named a concurrency token the stored proposal no longer
    /// carries (ADR-PA34): somebody else decided it in the meantime.
    /// <para>
    /// <b>Refused, never retried — and that is the distinction from the retry above.</b> The
    /// allocation retry loses a race for a mapping <i>version</i>, where re-reading and re-allocating
    /// still produces the decision the caller asked for. A stale token means the <i>proposal</i>
    /// moved, so retrying would decide something the caller has never seen. An omitted token opts out
    /// entirely, for a caller with no prior view to be stale.
    /// </para>
    /// </summary>
    internal static void EnsureExpectedToken(StoredMappingProposal stored, string? expectedToken)
    {
        if (expectedToken is null || string.Equals(stored.ETag, expectedToken, StringComparison.Ordinal))
        {
            return;
        }

        var decided = stored.Proposal.DecidedBy is { } actor ? $", decided by '{actor}'" : string.Empty;
        throw new MappingConcurrencyConflictException(
            nameof(IMappingGovernanceService),
            [$"proposal '{stored.Proposal.ProposalId}' changed while it was being decided: it is now "
             + $"'{stored.Proposal.State.Name}'{decided}. Re-read it before deciding again."]);
    }

    /// <summary>Appends the record, then performs the change — compensating the record if the change fails (ADR-PA30's ordering).</summary>
    internal async Task RecordThenAsync(MappingGovernanceDecision decision, Func<Task> change, CancellationToken cancellationToken)
    {
        var recordId = await recorder.RecordAsync(decision, cancellationToken);
        try
        {
            await change();
        }
        catch (Exception ex)
        {
            await recorder.RecordCompensationAsync(decision, recordId, ex.Message, CancellationToken.None);
            throw;
        }
    }

    /// <summary>The role's current fleet version, which a canary proposal falls back to (the S6.6 verdict).</summary>
    internal async Task<int?> FindFleetVersionAsync(string roleId, CancellationToken cancellationToken)
    {
        if (await store.FindCurrentVersionAsync(roleId, cancellationToken) is not { } current)
        {
            return null;
        }

        var mapping = await store.FindMappingVersionAsync(roleId, current, cancellationToken);
        return mapping is null ? null : mapping.Ring == RolloutRing.Fleet ? mapping.MappingVersion : mapping.PredecessorFleetVersion;
    }

    /// <summary>The pending proposal, with the caller's ring, version, approver and effective-at replaced by the service's.</summary>
    internal static MappingChangeProposal BuildProposal(MappingChange change, string proposedBy, DateTime now, int? fleetVersion) => new()
    {
        ProposalId = Guid.NewGuid().ToString("N"),
        RoleId = change.RoleId,
        State = MappingProposalState.PendingApproval,
        ProposedBy = proposedBy,
        ProposedAtUtc = now,
        PredecessorFleetVersion = fleetVersion,
        Change = change with
        {
            ProposedMapping = change.ProposedMapping with
            {
                MappingVersion = 0,
                Ring = RolloutRing.Canary,
                ApprovedBy = string.Empty,
                EffectiveFromUtc = now,
                // The governed reason is the change's own; a mapping's ChangeReason carried in by the
                // caller would be the previous release's text, stored where it reads as this one's.
                ChangeReason = change.Reason,
                PredecessorFleetVersion = fleetVersion,
            },
        },
    };

    /// <summary>The immutable version document an approval or promotion creates.</summary>
    internal static RoleMapping BuildMapping(MappingChangeProposal proposal, int version, string actor, string reason, DateTime now, MappingProposalState target)
    {
        var fleet = target == MappingProposalState.Promoted;

        return proposal.Change.ProposedMapping with
        {
            MappingVersion = version,
            Ring = fleet ? RolloutRing.Fleet : RolloutRing.Canary,
            CanaryPercent = fleet ? 0 : proposal.Change.ProposedMapping.CanaryPercent,
            ApprovedBy = actor,
            EffectiveFromUtc = now,
            ChangeReason = reason,
            PredecessorFleetVersion = fleet ? null : proposal.PredecessorFleetVersion,
        };
    }

    /// <summary>The proposal as the decision leaves it.</summary>
    internal static MappingChangeProposal ApplyDecision(
        MappingChangeProposal proposal, RoleMapping mapping, string actor, string reason, DateTime now, MappingProposalState target)
    {
        var decided = proposal with { State = target, DecidedBy = actor, DecidedAtUtc = now, DecisionReason = reason };

        return target == MappingProposalState.Promoted
            ? decided with { PromotedVersion = mapping.MappingVersion }
            : decided with { MappingVersion = mapping.MappingVersion, ApprovedBy = actor, EffectiveFromUtc = now };
    }

    /// <summary>The audit event a state move emits.</summary>
    internal static string EventFor(MappingProposalState target) =>
        target == MappingProposalState.Promoted ? MappingGovernanceEventTypes.Promoted : MappingGovernanceEventTypes.Approved;

    /// <summary>A decision record for a proposal-shaped change.</summary>
    internal static MappingGovernanceDecision Decision(string eventType, MappingChangeProposal proposal, string actor, string reason, DateTime now) => new()
    {
        EventType = eventType,
        RoleId = proposal.RoleId,
        ProposalId = proposal.ProposalId,
        Actor = actor,
        Reason = reason,
        OccurredAtUtc = now,
        State = proposal.State,
    };

    /// <summary>A decision record for a rollback, which has no proposal of its own.</summary>
    internal static MappingGovernanceDecision RollbackDecision(
        string roleId, string actor, string reason, DateTime now, int previous, int toVersion, RolloutRing ring) => new()
    {
        EventType = MappingGovernanceEventTypes.RolledBack,
        RoleId = roleId,
        Actor = actor,
        Reason = reason,
        OccurredAtUtc = now,
        FromVersion = previous,
        ToVersion = toVersion,
        Ring = ring,
        State = MappingProposalState.RolledBack,
    };

    /// <inheritdoc />
    [Obsolete("Superseded by ProposeAsync(change, proposedBy, cancellationToken); see IMappingGovernanceService.")]
    public Task<MappingChangeProposal> ProposeChangeAsync(MappingChange change, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A proposal must name the principal that made it (ADR-E8). Use ProposeAsync(change, proposedBy, cancellationToken).");

    /// <inheritdoc />
    [Obsolete("Superseded by ApproveAsync(roleId, proposalId, approverId, reason, cancellationToken); see IMappingGovernanceService.")]
    public Task ApproveAsync(string proposalId, string approverId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("An approval needs its role and a reason for the governance record. Use ApproveAsync(roleId, proposalId, approverId, reason, expectedConcurrencyToken, cancellationToken).");

    /// <inheritdoc />
    [Obsolete("Superseded by RollbackToVersionAsync(roleId, toVersion, actor, reason, cancellationToken); see IMappingGovernanceService.")]
    public Task RollbackAsync(string roleId, int toVersion, string reason, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A rollback must be attributed and audited, and must refuse a missing or shadow target (ADR-PA32). Use RollbackToVersionAsync(roleId, toVersion, actor, reason, cancellationToken).");
}
