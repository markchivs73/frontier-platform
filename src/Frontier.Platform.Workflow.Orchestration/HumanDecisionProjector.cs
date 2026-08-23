using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Projects an <see cref="ExecutionSnapshot"/>'s <see cref="HitlDecision"/>s into
/// <see cref="HumanDecisionRecord"/>s (doc 05 §4 step 4). Drops
/// <see cref="HitlDecision.RollbackToNodeId"/> — <see cref="HumanDecisionRecord"/> has no
/// equivalent field (S5.1's frozen contract); the rollback target is operational state,
/// not an audit fact.
/// </summary>
internal static class HumanDecisionProjector
{
    /// <summary>Maps every recorded decision to its <see cref="HumanDecisionRecord"/> projection.</summary>
    internal static IReadOnlyList<HumanDecisionRecord> Project(IReadOnlyList<HitlDecision> decisions) =>
        decisions.Select(ToHumanDecisionRecord).ToArray();

    /// <summary>Maps one <see cref="HitlDecision"/> to a <see cref="HumanDecisionRecord"/>.</summary>
    internal static HumanDecisionRecord ToHumanDecisionRecord(HitlDecision decision) => new()
    {
        GateId = decision.GateId,
        RequestId = decision.RequestId,
        ApproverId = decision.ApproverId,
        Kind = decision.Kind,
        Notes = decision.Notes,
        DecidedAtUtc = decision.DecidedAtUtc,
    };
}
