
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Consumer-owned port (S11.6, ADR-PA2): the audit consolidator's read-only view of the
/// latest execution checkpoint. ArtifactState owns the <c>execution-snapshots</c>
/// projection (writer + topology check); Host adapts its <c>ISnapshotStore</c> to this
/// port — the previous Cosmos reader in Platform.Audit was a duplicated read path over a
/// container it never owned, deleted with this port''s introduction.
/// </summary>
public interface IExecutionSnapshotReader
{
    /// <summary>
    /// Returns the most recent checkpoint (<c>is_latest = true</c>) for
    /// <paramref name="executionId"/>, or <see langword="null"/> if none exists yet.
    /// </summary>
    Task<ExecutionSnapshot?> GetLatestAsync(string executionId, string engagementId, CancellationToken cancellationToken);
}
