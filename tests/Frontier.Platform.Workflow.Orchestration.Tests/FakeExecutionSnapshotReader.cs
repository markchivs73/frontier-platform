
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Orchestration;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>In-memory <see cref="IExecutionSnapshotReader"/> test double for S5.4 consolidator tests.</summary>
internal sealed class FakeExecutionSnapshotReader(ExecutionSnapshot? snapshot) : IExecutionSnapshotReader
{
    /// <inheritdoc />
    public Task<ExecutionSnapshot?> GetLatestAsync(string executionId, string engagementId, CancellationToken cancellationToken) =>
        Task.FromResult(snapshot);
}
