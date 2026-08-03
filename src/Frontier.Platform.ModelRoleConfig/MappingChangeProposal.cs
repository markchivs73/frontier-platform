namespace Frontier.Platform.ModelRoleConfig;

/// <summary>A <see cref="MappingChange"/> recorded as a proposal awaiting approval (doc 08 §7).</summary>
public sealed record MappingChangeProposal
{
    /// <summary>The proposal's identifier, used by <see cref="IMappingGovernanceService.ApproveAsync"/>.</summary>
    public required string ProposalId { get; init; }

    /// <summary>The proposed change.</summary>
    public required MappingChange Change { get; init; }

    /// <summary>When this proposal was recorded.</summary>
    public required DateTime ProposedAtUtc { get; init; }
}
