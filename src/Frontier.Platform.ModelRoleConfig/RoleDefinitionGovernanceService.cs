using Frontier.Platform.Abstractions;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IRoleDefinitionGovernanceService"/> (ADR-PA40), the definition twin of
/// <see cref="MappingGovernanceService"/>: every decision is recorded before it is attempted and
/// compensated if it does not land, every state move is guarded by the proposal's ETag, and every
/// definition version is allocated by a create-only write on <c>{roleId}:definition:v{n}</c>.
/// </summary>
internal sealed class RoleDefinitionGovernanceService(
    IRoleDefinitionStore store,
    RoleContractResolver contracts,
    IMappingDecisionRecorder recorder,
    TimeProvider timeProvider,
    IOptions<MappingGovernanceOptions> options) : IRoleDefinitionGovernanceService
{
    /// <inheritdoc />
    public async Task<RoleDefinitionProposal> ProposeAsync(RoleDefinition definition, string proposedBy, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        MappingProposalRules.EnsureAttributed(proposedBy, reason);
        var proposed = RoleDefinitionProposalRules.Unstamped(definition);
        RoleDefinitionProposalRules.ValidateDefinition(proposed);
        await EnsureNoUndecidedProposalAsync(proposed.RoleId, cancellationToken);
        var current = await store.FindCurrentDefinitionAsync(proposed.RoleId, cancellationToken);
        RoleDefinitionProposalRules.EnsureBackingUnchanged(current, proposed);
        await contracts.StampAsync(proposed, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var proposal = BuildProposal(proposed, proposedBy, reason, now, current?.DefinitionVersion);
        proposal.Validate();
        var decision = Decision(RoleDefinitionGovernanceEventTypes.Proposed, proposal, proposedBy, reason, now);
        await RecordThenAsync(decision, () => store.CreateProposalAsync(proposal, cancellationToken), cancellationToken);
        return proposal;
    }

    /// <inheritdoc />
    public async Task<RoleDefinitionProposal> ApproveAsync(
        string roleId, string proposalId, string approverId, string reason, string? expectedConcurrencyToken, CancellationToken cancellationToken)
    {
        EnsureDecisionArguments(roleId, proposalId, approverId, reason);
        var retry = options.Value;
        for (var attempt = 1; attempt <= retry.DecisionMaxAttempts; attempt++)
        {
            var stored = await LoadForTransitionAsync(roleId, proposalId, MappingProposalState.Approved, expectedConcurrencyToken, cancellationToken);
            EnsureDecisionAuthority(stored.Proposal, approverId, MappingProposalState.Approved);
            if (await TryApproveOnceAsync(stored, approverId, reason, cancellationToken) is { } approved)
            {
                return approved;
            }

            await MappingGovernanceService.DelayBeforeRetryAsync(attempt, retry, cancellationToken);
        }

        throw new MappingGovernanceException(
            $"The decision on definition proposal '{proposalId}' for role '{roleId}' kept losing its definition-version allocation to concurrent writers; the {retry.DecisionMaxAttempts} attempts are exhausted.");
    }

    /// <inheritdoc />
    public Task<RoleDefinitionProposal> RejectAsync(
        string roleId, string proposalId, string approverId, string reason, string? expectedConcurrencyToken, CancellationToken cancellationToken) =>
        DecideAsync(roleId, proposalId, approverId, reason, MappingProposalState.Rejected, expectedConcurrencyToken, cancellationToken);

    /// <inheritdoc />
    public Task<RoleDefinitionProposal> WithdrawAsync(
        string roleId, string proposalId, string actor, string reason, string? expectedConcurrencyToken, CancellationToken cancellationToken) =>
        DecideAsync(roleId, proposalId, actor, reason, MappingProposalState.Withdrawn, expectedConcurrencyToken, cancellationToken);

    /// <inheritdoc />
    public async Task<RoleDefinitionProposal?> GetProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);

        var stored = await store.FindProposalAsync(roleId, proposalId, cancellationToken);
        return stored is null ? null : stored.Proposal with { ConcurrencyToken = stored.ETag };
    }

    /// <inheritdoc />
    public Task<RoleDefinitionProposalPage> ListProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, MappingProposalQuery.MaxPageSize, nameof(query));

        return store.QueryProposalsAsync(query, cancellationToken);
    }

    /// <summary>
    /// One definition proposal per role at a time, as for mappings (Mark's call, 2026-09-16), and
    /// separate from the role's mapping-proposal slot. Refused before the audit record, so nothing is written.
    /// </summary>
    internal async Task EnsureNoUndecidedProposalAsync(string roleId, CancellationToken cancellationToken)
    {
        if (await store.FindUndecidedProposalAsync(roleId, cancellationToken) is not { } existing)
        {
            return;
        }

        throw new MappingProposalAlreadyPendingException(
            nameof(IRoleDefinitionGovernanceService),
            [$"role '{roleId}' already has definition proposal '{existing.Proposal.ProposalId}' awaiting a decision, raised by "
             + $"'{existing.Proposal.ProposedBy}'; decide that proposal before raising another."]);
    }

    /// <summary>
    /// One stamp-allocate-audit-write pass: the approved proposal, or <see langword="null"/> when
    /// another writer won. The refs are resolved again here, so a contract retired since propose is
    /// refused, and backing is checked again against whatever definition is current now.
    /// </summary>
    internal async Task<RoleDefinitionProposal?> TryApproveOnceAsync(
        StoredRoleDefinitionProposal stored, string approverId, string reason, CancellationToken cancellationToken)
    {
        var proposal = stored.Proposal;
        var current = await store.FindCurrentDefinitionAsync(proposal.RoleId, cancellationToken);
        RoleDefinitionProposalRules.EnsureBackingUnchanged(current, proposal.ProposedDefinition);
        var stamped = await contracts.StampAsync(proposal.ProposedDefinition, cancellationToken);
        var next = NextVersion(await store.ListDefinitionVersionsAsync(proposal.RoleId, cancellationToken));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var approved = ApplyApproval(proposal, stamped, next, approverId, reason, now);
        var decision = Decision(RoleDefinitionGovernanceEventTypes.Approved, approved, approverId, reason, now) with
        {
            FromVersion = current?.DefinitionVersion,
            ToVersion = next,
        };

        var recordId = await recorder.RecordAsync(decision, cancellationToken);
        var token = await TryLandAsync(approved, stored.ETag, decision, recordId, cancellationToken);
        return token is null ? null : approved with { ConcurrencyToken = token };
    }

    /// <summary>Runs the conditional batch, compensating the audit record when it does not land.</summary>
    internal async Task<string?> TryLandAsync(
        RoleDefinitionProposal approved, string etag, MappingGovernanceDecision decision, string recordId, CancellationToken cancellationToken)
    {
        string? landed;
        try
        {
            landed = await store.TryAllocateDefinitionAsync(approved.ProposedDefinition, approved, etag, cancellationToken);
        }
        catch (Exception ex)
        {
            await recorder.RecordCompensationAsync(decision, recordId, ex.Message, CancellationToken.None);
            throw;
        }

        if (landed is null)
        {
            await recorder.RecordCompensationAsync(decision, recordId, "another writer allocated this definition version first; the decision was retried.", CancellationToken.None);
        }

        return landed;
    }

    /// <summary>Reject and withdraw share one shape: no version, one ETag-guarded replace.</summary>
    internal async Task<RoleDefinitionProposal> DecideAsync(
        string roleId, string proposalId, string actor, string reason, MappingProposalState target, string? expectedToken, CancellationToken cancellationToken)
    {
        EnsureDecisionArguments(roleId, proposalId, actor, reason);
        var retry = options.Value;
        for (var attempt = 1; attempt <= retry.DecisionMaxAttempts; attempt++)
        {
            var stored = await LoadForTransitionAsync(roleId, proposalId, target, expectedToken, cancellationToken);
            EnsureDecisionAuthority(stored.Proposal, actor, target);
            if (await TryDecideOnceAsync(stored, actor, reason, target, cancellationToken) is { } decided)
            {
                return decided;
            }

            await MappingGovernanceService.DelayBeforeRetryAsync(attempt, retry, cancellationToken);
        }

        throw new MappingGovernanceException($"The decision on definition proposal '{proposalId}' for role '{roleId}' kept losing to concurrent writers; the {retry.DecisionMaxAttempts} attempts are exhausted.");
    }

    /// <summary>One audit-then-replace pass for a reject or withdraw: the decided proposal, or <see langword="null"/> when another decision won.</summary>
    internal async Task<RoleDefinitionProposal?> TryDecideOnceAsync(
        StoredRoleDefinitionProposal stored, string actor, string reason, MappingProposalState target, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updated = stored.Proposal with { State = target, DecidedBy = actor, DecidedAtUtc = now, DecisionReason = reason };
        var decision = Decision(EventFor(target), updated, actor, reason, now);

        var recordId = await recorder.RecordAsync(decision, cancellationToken);
        if (await store.TryReplaceProposalAsync(updated, stored.ETag, cancellationToken) is { } token)
        {
            return updated with { ConcurrencyToken = token };
        }

        await recorder.RecordCompensationAsync(decision, recordId, "another decision reached this proposal first; the decision was retried.", CancellationToken.None);
        return null;
    }

    /// <summary>
    /// Reads a proposal and refuses the decision unless it may legally be taken: the caller's token is
    /// current (when supplied, checked first, ADR-PA34) and the move is legal from where the proposal is.
    /// </summary>
    internal async Task<StoredRoleDefinitionProposal> LoadForTransitionAsync(
        string roleId, string proposalId, MappingProposalState target, string? expectedToken, CancellationToken cancellationToken)
    {
        var stored = await store.FindProposalAsync(roleId, proposalId, cancellationToken)
            ?? throw new UnknownMappingProposalException(nameof(IRoleDefinitionGovernanceService), [$"role '{roleId}' has no definition proposal '{proposalId}'."]);

        var proposal = stored.Proposal;
        MappingProposalRules.EnsureExpectedToken(
            nameof(IRoleDefinitionGovernanceService), proposal.ProposalId, proposal.State, proposal.DecidedBy, stored.ETag, expectedToken);
        MappingProposalRules.EnsureTransition(nameof(RoleDefinitionProposal), proposal.ProposalId, proposal.State, target);
        return stored;
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

    /// <summary>Only the proposer may withdraw; only someone else may approve or reject (the mapping rules, ADR-PA40).</summary>
    internal static void EnsureDecisionAuthority(RoleDefinitionProposal proposal, string actor, MappingProposalState target)
    {
        if (target == MappingProposalState.Withdrawn)
        {
            MappingProposalRules.EnsureProposerOnly(nameof(RoleDefinitionProposal), proposal.ProposalId, proposal.ProposedBy, actor);
        }
        else
        {
            MappingProposalRules.EnsureDistinctApprover(nameof(RoleDefinitionProposal), proposal.ProposalId, proposal.ProposedBy, actor);
        }
    }

    /// <summary>Every decision names its role and proposal and is attributed and reasoned.</summary>
    internal static void EnsureDecisionArguments(string roleId, string proposalId, string actor, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        MappingProposalRules.EnsureAttributed(actor, reason);
    }

    /// <summary>The next definition version: one past the highest stored, so a new role's first is v1.</summary>
    internal static int NextVersion(IReadOnlyList<int> versions) =>
        versions.Count == 0 ? 1 : versions.Max() + 1;

    /// <summary>The pending proposal, carrying the unstamped definition.</summary>
    internal static RoleDefinitionProposal BuildProposal(RoleDefinition definition, string proposedBy, string reason, DateTime now, int? predecessor) => new()
    {
        ProposalId = Guid.NewGuid().ToString("N"),
        RoleId = definition.RoleId,
        State = MappingProposalState.PendingApproval,
        ProposedDefinition = definition,
        Reason = reason,
        ProposedBy = proposedBy,
        ProposedAtUtc = now,
        PredecessorDefinitionVersion = predecessor,
    };

    /// <summary>The proposal as approval leaves it, with the stamped definition it mints.</summary>
    internal static RoleDefinitionProposal ApplyApproval(
        RoleDefinitionProposal proposal, RoleDefinition stamped, int version, string approverId, string reason, DateTime now) => proposal with
    {
        State = MappingProposalState.Approved,
        ProposedDefinition = stamped with { DefinitionVersion = version, ApprovedBy = approverId, EffectiveFromUtc = now },
        DefinitionVersion = version,
        ApprovedBy = approverId,
        EffectiveFromUtc = now,
        DecidedBy = approverId,
        DecidedAtUtc = now,
        DecisionReason = reason,
    };

    /// <summary>The audit event a reject or withdraw emits.</summary>
    internal static string EventFor(MappingProposalState target) =>
        target == MappingProposalState.Withdrawn ? RoleDefinitionGovernanceEventTypes.Withdrawn : RoleDefinitionGovernanceEventTypes.Rejected;

    /// <summary>A decision record for a definition proposal; the subject is the role.</summary>
    internal static MappingGovernanceDecision Decision(string eventType, RoleDefinitionProposal proposal, string actor, string reason, DateTime now) => new()
    {
        EventType = eventType,
        RoleId = proposal.RoleId,
        ProposalId = proposal.ProposalId,
        Actor = actor,
        Reason = reason,
        OccurredAtUtc = now,
        State = proposal.State,
    };
}
