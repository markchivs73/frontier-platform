namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>Fixtures for the ADR-PA40 evidence tests (ADR-E27 D16): an agent-backed role, its chain, and its evidence.</summary>
internal static class EvaluationEvidenceSamples
{
    /// <summary>A well-formed manual evidence ref.</summary>
    internal const string ManualRef = "eval-run:manual:2026-10-30-record-pass";

    /// <summary>A well-formed kit evidence ref.</summary>
    internal const string KitRef = "eval-run:kit:run-0042";

    /// <summary>The agent-backed role every evidence test proposes against.</summary>
    internal static RoleDefinition AgentRole => RoleDefinitionSamples.AgentBacked();

    /// <summary>The agent-backed role after a definition change moved its output contract.</summary>
    internal static RoleDefinition AgentRoleWithNewOutput => AgentRole with
    {
        DefinitionVersion = 3,
        OutputContractRef = "genealogy/record-pass-output/2.0",
        OutputContractHash = "sha256:" + new string('c', 64),
    };

    /// <summary>The registered agent an agent chain names.</summary>
    internal static AgentEntry Agent => new()
    {
        Provider = AgentEntry.A2aProvider,
        Currency = "USD",
        ResourceName = "com.azure.foundry/gen-record-pass",
        ResourceVersion = "1.0",
        CostPerInvocation = 0.02m,
    };

    /// <summary>A mapping of <see cref="AgentRole"/> onto <see cref="Agent"/>, carrying <paramref name="evidenceRef"/>.</summary>
    internal static RoleMapping AgentMapping(string? evidenceRef = ManualRef) => new()
    {
        RoleId = AgentRole.RoleId,
        MappingVersion = 0,
        Chain = [Agent],
        Ring = RolloutRing.Canary,
        CanaryPercent = 10,
        ChangeReason = "record-pass agent evaluated against v2 contracts",
        ApprovedBy = string.Empty,
        EffectiveFromUtc = FixedTimeProvider.Now,
        EvaluationEvidenceRef = evidenceRef,
    };

    /// <summary>A change proposing <see cref="AgentMapping"/>.</summary>
    internal static MappingChange AgentChange(string? evidenceRef = ManualRef) => new()
    {
        RoleId = AgentRole.RoleId,
        Reason = "record-pass agent evaluated against v2 contracts",
        ProposedMapping = AgentMapping(evidenceRef),
    };

    /// <summary>A definitions store whose current definition is <paramref name="role"/>.</summary>
    internal static FakeRoleDefinitionStore Defining(RoleDefinition role) => new FakeRoleDefinitionStore().WithDefinition(role);
}
