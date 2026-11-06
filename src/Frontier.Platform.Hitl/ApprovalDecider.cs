using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Hitl;

/// <summary>
/// The decision loop of <see cref="IApprovalStore.DecideAsync"/> (doc 06 §9): read, check, replace under
/// the read's version, and on a lost race try once more before telling the caller who won. Moved out
/// of the coverage-excluded <see cref="CosmosApprovalStore"/> (S13.193) so the retry and the lost-update
/// handling are measured by the unit gate; the adapter keeps only the SDK calls. Since S13.149 the loop
/// itself is <see cref="ApprovalTransitioner"/>, shared with withdrawal and escalation.
/// </summary>
internal sealed class ApprovalDecider(IApprovalDocuments documents)
{
    /// <summary>Applies <paramref name="decision"/> to the request, or reports why it could not (see <see cref="IApprovalStore.DecideAsync"/>).</summary>
    internal async Task<ApprovalDecisionResult> DecideAsync(string engagementId, string approvalId, HitlDecision decision, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engagementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        ArgumentNullException.ThrowIfNull(decision);

        var result = await new ApprovalTransitioner(documents).TransitionAsync(
            engagementId, approvalId, ApprovalDecisionTransition.CanDecide, request => ApprovalDecisionTransition.Apply(request, decision), cancellationToken);
        return ToDecisionResult(result);
    }

    /// <summary>Maps the shared transition result onto <see cref="IApprovalStore.DecideAsync"/>'s own vocabulary.</summary>
    internal static ApprovalDecisionResult ToDecisionResult(ApprovalTransitionResult result) =>
        result.Outcome == ApprovalTransitionOutcome.Applied ? ApprovalDecisionResult.Decided(result.Request!)
        : result.Outcome == ApprovalTransitionOutcome.Refused ? ApprovalDecisionResult.AlreadyDecided(result.Request!)
        : ApprovalDecisionResult.NotFound();
}
