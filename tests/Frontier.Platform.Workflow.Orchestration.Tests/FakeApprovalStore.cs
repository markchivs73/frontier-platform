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
}
