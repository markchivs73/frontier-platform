using Microsoft.Extensions.Options;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>Fixtures for the S13.101 mapping-governance tests (ADR-PA32).</summary>
internal static class MappingProposalSamples
{
    internal const string RoleId = "deep-reasoning";

    internal const string Proposer = "user:oid-proposer";

    internal const string Approver = "user:oid-approver";

    /// <summary>The seeded fleet mapping a proposal is measured against.</summary>
    internal static RoleMapping FleetV1 => Phase1RoleCatalogue.DeepReasoningMappingV1;

    /// <summary>A well-formed proposed change.</summary>
    internal static MappingChange Change(string reason = "Fable 5 eval: pass rate +6pts, cost -22% vs v1") => new()
    {
        RoleId = RoleId,
        Reason = reason,
        ProposedMapping = FleetV1 with
        {
            Chain = [FleetV1.Chain[1]],
            Ring = RolloutRing.Canary,
            CanaryPercent = 10,
        },
    };

    /// <summary>A pending proposal as the service would have recorded it.</summary>
    internal static MappingChangeProposal Pending(string proposalId = "p-1") => new()
    {
        ProposalId = proposalId,
        RoleId = RoleId,
        State = MappingProposalState.PendingApproval,
        ProposedBy = Proposer,
        ProposedAtUtc = FixedTimeProvider.Now,
        PredecessorFleetVersion = 1,
        Change = Change() with
        {
            ProposedMapping = Change().ProposedMapping with
            {
                MappingVersion = 0,
                ApprovedBy = string.Empty,
                EffectiveFromUtc = FixedTimeProvider.Now,
                PredecessorFleetVersion = 1,
            },
        },
    };

    /// <summary>A shadow mapping version — stored, never served, and never <c>current</c>.</summary>
    internal static RoleMapping ShadowV2 => FleetV1 with
    {
        MappingVersion = 2,
        Ring = RolloutRing.Shadow,
        PredecessorFleetVersion = 1,
    };

    /// <summary>Retry settings with the delays removed, so a contention test does not sleep.</summary>
    internal static MappingGovernanceOptions FastRetry => new() { DecisionBaseDelayMs = 0, DecisionMaxDelayMs = 0 };

    /// <summary>The service under test, wired to <paramref name="store"/> and <paramref name="recorder"/>.</summary>
    internal static MappingGovernanceService Service(
        FakeMappingProposalStore store,
        FakeMappingDecisionRecorder recorder,
        FakeRoleMappingWriter? writer = null,
        MappingGovernanceOptions? options = null) =>
        new(store, writer ?? new FakeRoleMappingWriter(), recorder, new FixedTimeProvider(FixedTimeProvider.Now), Options.Create(options ?? FastRetry));
}

/// <summary>Captures the <c>current</c> pointer writes a rollback makes.</summary>
internal sealed class FakeRoleMappingWriter : IRoleMappingWriter
{
    /// <summary>Every (role, version) the pointer was rewritten to.</summary>
    internal List<(string RoleId, int Version)> Writes { get; } = [];

    /// <summary>Makes the write throw, standing in for a store failure after the audit landed.</summary>
    internal Exception? Throws { get; set; }

    public Task WriteCurrentAsync(string roleId, int toVersion, CancellationToken ct)
    {
        if (Throws is { } failure)
        {
            throw failure;
        }

        Writes.Add((roleId, toVersion));
        return Task.CompletedTask;
    }
}
