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
}
