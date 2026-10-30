using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>Fixtures for the ADR-PA40 definition-governance tests.</summary>
internal static class RoleDefinitionProposalSamples
{
    internal const string AgentRoleId = "gen-record-pass";

    internal const string InputRef = "genealogy/record-pass-input/1.0";

    internal const string OutputRef = "genealogy/record-pass-output/1.0";

    internal const string Proposer = MappingProposalSamples.Proposer;

    internal const string Approver = MappingProposalSamples.Approver;

    internal const string Reason = "Slice 6: the record pass is served by a registered agent";

    /// <summary>A retry profile with no real waiting, for the race tests.</summary>
    internal static MappingGovernanceOptions FastRetry => new() { DecisionMaxAttempts = 3, DecisionBaseDelayMs = 0, DecisionMaxDelayMs = 0 };

    /// <summary>A catalogue holding both agent contracts.</summary>
    internal static FakeContractCatalog Catalog() => new FakeContractCatalog().With(InputRef).With(OutputRef);

    /// <summary>An unstamped agent-backed definition, as a caller proposes it.</summary>
    internal static RoleDefinition AgentDefinition() => RoleDefinitionSamples.AgentBacked(AgentRoleId) with
    {
        InputContractHash = null,
        OutputContractHash = null,
        DefinitionVersion = null,
        ApprovedBy = null,
        EffectiveFromUtc = null,
    };

    /// <summary>The model-backed Phase 1 role <c>deep-reasoning</c>, unstamped.</summary>
    internal static RoleDefinition ModelDefinition() => RoleDefinitionSamples.Phase1Role("deep-reasoning");

    /// <summary>A pending proposal for <paramref name="definition"/>, as the service would have recorded it.</summary>
    internal static RoleDefinitionProposal Pending(RoleDefinition? definition = null, string proposalId = "d-1")
    {
        var proposed = definition ?? AgentDefinition();
        return new RoleDefinitionProposal
        {
            ProposalId = proposalId,
            RoleId = proposed.RoleId,
            State = MappingProposalState.PendingApproval,
            ProposedDefinition = proposed,
            Reason = Reason,
            ProposedBy = Proposer,
            ProposedAtUtc = FixedTimeProvider.Now,
        };
    }

    /// <summary>The service under test, on a fixed clock.</summary>
    internal static RoleDefinitionGovernanceService Service(
        IRoleDefinitionStore store, FakeMappingDecisionRecorder recorder, FakeContractCatalog? catalog = null) =>
        new(store, new RoleContractResolver(catalog ?? Catalog()), recorder, new FixedTimeProvider(FixedTimeProvider.Now), Options.Create(FastRetry));
}
