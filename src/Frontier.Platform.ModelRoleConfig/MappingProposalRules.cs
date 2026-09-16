using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The structural rules a proposal and its proposed chain must satisfy (ADR-PA32). Pure, so every
/// branch is reachable from a test without a store. Every failure is a typed
/// <see cref="MappingGovernanceRefusalException"/> (ADR-PA34) — still a
/// <see cref="ContractViolationException"/>, so still permanent and never retried (invariant 7),
/// but each refusal now has its own catchable type so a consumer can map its status code.
/// </summary>
internal static class MappingProposalRules
{
    /// <summary>Throws unless <paramref name="proposal"/> is internally consistent.</summary>
    internal static void ValidateProposal(MappingChangeProposal proposal)
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(proposal.ProposalId))
        {
            violations.Add("proposal_id is required.");
        }

        if (string.IsNullOrWhiteSpace(proposal.RoleId))
        {
            violations.Add("role_id is required.");
        }

        if (string.IsNullOrWhiteSpace(proposal.ProposedBy))
        {
            violations.Add("proposed_by is required; a proposal is attributed to the principal that made it (ADR-E8).");
        }

        AddDecidedStateViolations(proposal, violations);
        Throw(violations);
    }

    /// <summary>A decided proposal must carry the attribution its state implies.</summary>
    internal static void AddDecidedStateViolations(MappingChangeProposal proposal, List<string> violations)
    {
        if (proposal.State == MappingProposalState.Approved && proposal.MappingVersion is null)
        {
            violations.Add("an approved proposal must carry the mapping_version allocated for it.");
        }

        if (proposal.State == MappingProposalState.Approved && string.IsNullOrWhiteSpace(proposal.ApprovedBy))
        {
            violations.Add("an approved proposal must name its approver.");
        }

        if (proposal.State == MappingProposalState.Promoted && proposal.PromotedVersion is null)
        {
            violations.Add("a promoted proposal must carry the fleet version allocated for it.");
        }

        if (proposal.State == MappingProposalState.RolledBack && proposal.RolledBackToVersion is null)
        {
            violations.Add("a rolled-back proposal must name the version it was rolled back to.");
        }
    }

    /// <summary>
    /// Throws unless <paramref name="change"/> may be proposed: a role, a reason, and a chain that
    /// <see cref="ChainShape"/> accepts and whose every entry names a resolvable target (K7).
    /// </summary>
    internal static void ValidateChange(MappingChange change)
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(change.RoleId))
        {
            violations.Add("role_id is required.");
        }

        if (string.IsNullOrWhiteSpace(change.Reason))
        {
            violations.Add("reason is required; a mapping change is a governed release, not an edit (doc 08 §2 principle 3).");
        }

        AddChainViolations(change, violations);
        Throw(violations);
    }

    /// <summary>The proposed chain's own rules: non-empty, well-shaped (ADR-PA27), and every target resolvable (K7).</summary>
    internal static void AddChainViolations(MappingChange change, List<string> violations)
    {
        var chain = change.ProposedMapping.Chain;
        if (chain.Count == 0)
        {
            violations.Add("the proposed chain must have at least a primary entry.");
            return;
        }

        violations.AddRange(ChainShape.Violations(chain));

        if (!string.Equals(change.RoleId, change.ProposedMapping.RoleId, StringComparison.Ordinal))
        {
            violations.Add($"the proposed mapping is for role '{change.ProposedMapping.RoleId}', but the change names '{change.RoleId}'.");
        }

        if (change.ProposedMapping.CanaryPercent is < 1 or > 100)
        {
            violations.Add(
                "canary_percent must be between 1 and 100; 0 would approve a mapping that reports itself live in the canary ring "
                + "while serving no engagement at all (Mark's call, 2026-09-16). A deliberately staged, non-serving version is what the shadow stage is for.");
        }

        AddTargetViolations(chain, violations);
    }

    /// <summary>
    /// K7's half that this library can enforce: a chain entry whose target id is absent or blank
    /// names a model the resolver could never serve, so the proposal is refused rather than stored
    /// and discovered at the first invocation.
    /// </summary>
    internal static void AddTargetViolations(IReadOnlyList<ChainEntry> chain, List<string> violations)
    {
        for (var index = 0; index < chain.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(chain[index].TargetId))
            {
                violations.Add($"chain entry {index} names no target; a model id that does not resolve cannot be proposed (K7).");
            }
        }
    }

    /// <summary>Throws unless <paramref name="from"/> may move to <paramref name="to"/>.</summary>
    internal static void EnsureTransition(MappingChangeProposal proposal, MappingProposalState to)
    {
        if (!proposal.State.CanTransitionTo(to))
        {
            throw new IllegalMappingTransitionException(
                nameof(MappingChangeProposal),
                [$"proposal '{proposal.ProposalId}' is '{proposal.State.Name}' and cannot move to '{to.Name}' (doc 08 §7)."]);
        }
    }

    /// <summary>
    /// The distinct-approver rule (<c>requireDistinctApprover</c>, doc 15 §4): the principal that
    /// proposed a change may never be the one that <b>decides</b> it. Applied to rejection as well as
    /// approval (Mark's call, 2026-09-16) — a rejection is a recorded decision on someone else's
    /// change, and a proposer who wants their own idea dead withdraws it.
    /// </summary>
    internal static void EnsureDistinctApprover(MappingChangeProposal proposal, string approverId)
    {
        if (string.Equals(proposal.ProposedBy, approverId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DistinctApproverRequiredException(
                nameof(MappingChangeProposal),
                [$"proposal '{proposal.ProposalId}' was proposed by '{proposal.ProposedBy}', who cannot also decide it (requireDistinctApprover); withdraw it instead."]);
        }
    }

    /// <summary>
    /// Withdrawal is the proposer's own route for killing their own idea, so only they may take it
    /// (Mark's call, 2026-09-16). Another principal ends a proposal by <b>rejecting</b> it, which is a
    /// recorded decision on someone else's change rather than a silent retraction.
    /// </summary>
    internal static void EnsureProposerOnly(MappingChangeProposal proposal, string actor)
    {
        if (!string.Equals(proposal.ProposedBy, actor, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProposerOnlyWithdrawalException(
                nameof(MappingChangeProposal),
                [$"proposal '{proposal.ProposalId}' was proposed by '{proposal.ProposedBy}', and only they may withdraw it; another principal must reject it instead."]);
        }
    }

    /// <summary>Throws a permanent violation listing <paramref name="violations"/>, if there are any.</summary>
    internal static void Throw(List<string> violations)
    {
        if (violations.Count > 0)
        {
            throw new InvalidMappingChangeException(nameof(MappingChangeProposal), violations);
        }
    }

    /// <summary>Throws unless <paramref name="actor"/> and <paramref name="reason"/> are both present (ADR-E8, doc 08 §7).</summary>
    internal static void EnsureAttributed(string actor, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
    }
}
