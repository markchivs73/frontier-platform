using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary><see cref="IApprovalStore"/> double recording the last upserted request.</summary>
internal sealed class FakeApprovalStore : IApprovalStore
{
    /// <summary>The most recent request passed to <see cref="UpsertAsync"/>, if any.</summary>
    internal ApprovalRequest? UpsertedRequest { get; private set; }

    /// <inheritdoc />
    public Task UpsertAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        Upserts++;
        UpsertedRequest = request;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequest>> GetDecidedAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApprovalRequest>>(UpsertedRequest is { Status: var status } && status == ApprovalRequestStatus.Decided ? [UpsertedRequest] : []);

    /// <inheritdoc />
    public Task<ApprovalDecisionResult> DecideAsync(string engagementId, string approvalId, HitlDecision decision, CancellationToken cancellationToken)
    {
        if (UpsertedRequest is null || UpsertedRequest.Id != approvalId || UpsertedRequest.EngagementId != engagementId)
        {
            return Task.FromResult(ApprovalDecisionResult.NotFound());
        }

        if (UpsertedRequest.Status != ApprovalRequestStatus.Pending && UpsertedRequest.Status != ApprovalRequestStatus.Escalated)
        {
            return Task.FromResult(ApprovalDecisionResult.AlreadyDecided(UpsertedRequest));
        }

        UpsertedRequest = UpsertedRequest with { Status = ApprovalRequestStatus.Decided, Decision = decision };
        return Task.FromResult(ApprovalDecisionResult.Decided(UpsertedRequest));
    }

    /// <summary>How many times <see cref="UpsertAsync"/> has been called — a refused escalation must not add one.</summary>
    internal int Upserts { get; private set; }

    /// <inheritdoc />
    public Task<ApprovalTransitionResult> WithdrawAsync(string engagementId, string approvalId, string withdrawnBy, string withdrawalReason, CancellationToken cancellationToken) =>
        Transition(engagementId, approvalId, r => r.Status == ApprovalRequestStatus.Pending || r.Status == ApprovalRequestStatus.Escalated, r => r with { Status = ApprovalRequestStatus.Withdrawn, WithdrawnBy = withdrawnBy, WithdrawalReason = withdrawalReason });

    /// <inheritdoc />
    public Task<ApprovalTransitionResult> EscalateAsync(string engagementId, string approvalId, CancellationToken cancellationToken) =>
        Transition(
            engagementId,
            approvalId,
            r => r.Status == ApprovalRequestStatus.Pending || r.Status == ApprovalRequestStatus.Escalated || (r.Status == ApprovalRequestStatus.Decided && r.Decision?.Kind == DecisionKind.Escalate),
            r => r with { Status = ApprovalRequestStatus.Escalated, Decision = null });

    /// <summary>The single stored request's guarded transition, mirroring the Cosmos store's rules.</summary>
    private Task<ApprovalTransitionResult> Transition(string engagementId, string approvalId, Func<ApprovalRequest, bool> canApply, Func<ApprovalRequest, ApprovalRequest> apply)
    {
        if (UpsertedRequest is null || UpsertedRequest.Id != approvalId || UpsertedRequest.EngagementId != engagementId)
        {
            return Task.FromResult(ApprovalTransitionResult.NotFound());
        }

        if (!canApply(UpsertedRequest))
        {
            return Task.FromResult(ApprovalTransitionResult.Refused(UpsertedRequest));
        }

        UpsertedRequest = apply(UpsertedRequest);
        return Task.FromResult(ApprovalTransitionResult.Applied(UpsertedRequest));
    }

    /// <summary>Seeds the stored request without counting it as an upsert.</summary>
    internal void Seed(ApprovalRequest request) => UpsertedRequest = request;
}
