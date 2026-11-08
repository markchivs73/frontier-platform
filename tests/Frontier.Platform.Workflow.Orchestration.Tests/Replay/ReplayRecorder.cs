using System.Collections.Concurrent;
using Frontier.Platform.Hitl;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests.Replay;

/// <summary>One scripted-model call: which node, on which worker, when it ran, and the dynamic-context epoch it was scheduled against.</summary>
internal sealed record AgentCall(string NodeId, string Worker, DateTimeOffset StartedAt, DateTimeOffset EndedAt, int? Epoch);

/// <summary>
/// One checkpoint the orchestrator wrote, the worker whose activity received it, when that write
/// started and when it landed: between the two the write is in flight.
/// </summary>
internal sealed record RecordedSnapshot(ExecutionSnapshot Snapshot, string Worker, DateTimeOffset StartedAt, DateTimeOffset RecordedAt);

/// <summary>One audit consolidation the orchestrator asked for, the worker that ran it, and when.</summary>
internal sealed record RecordedAudit(ConsolidateAuditInput Input, string Worker, DateTimeOffset RecordedAt);

/// <summary>One dynamic-context refresh the walk applied: the signal's reason, and when the activity ran.</summary>
internal sealed record AppliedRefresh(string Reason, DateTimeOffset AppliedAt);

/// <summary>
/// What the activities saw, shared across worker hosts so it outlives a worker restart the way a
/// real store would (S13.192). Activities run on the worker's thread pool, so every collection is
/// concurrent.
/// </summary>
internal sealed class ReplayRecorder
{
    private readonly ConcurrentQueue<AgentCall> _agentCalls = new();
    private readonly ConcurrentQueue<RecordedSnapshot> _snapshots = new();
    private readonly ConcurrentQueue<ApprovalRequest> _approvals = new();
    private readonly ConcurrentQueue<RecordedAudit> _audits = new();
    private readonly ConcurrentQueue<AppliedRefresh> _refreshes = new();
    private readonly ConcurrentQueue<string> _startedNodes = new();

    /// <summary>Every scripted-model call, in completion order.</summary>
    public IReadOnlyList<AgentCall> AgentCalls => [.. _agentCalls];

    /// <summary>Every checkpoint written, in arrival order.</summary>
    public IReadOnlyList<RecordedSnapshot> Snapshots => [.. _snapshots];

    /// <summary>Every approval request opened.</summary>
    public IReadOnlyList<ApprovalRequest> Approvals => [.. _approvals];

    /// <summary>The worker that ran each audit consolidation.</summary>
    public IReadOnlyList<string> AuditWorkers => [.. _audits.Select(audit => audit.Worker)];

    /// <summary>Every audit consolidation, in arrival order.</summary>
    public IReadOnlyList<RecordedAudit> Audits => [.. _audits];

    /// <summary>Each dynamic-context refresh applied, in the order applied.</summary>
    public IReadOnlyList<AppliedRefresh> Refreshes => [.. _refreshes];

    /// <summary>Records an applied refresh and returns how many have been applied, which the activity reports as the new epoch.</summary>
    public int AddRefresh(string reason)
    {
        _refreshes.Enqueue(new AppliedRefresh(reason, DateTimeOffset.UtcNow));
        return _refreshes.Count;
    }

    /// <summary>Every node whose scripted-model call has started, in start order.</summary>
    public IReadOnlyList<string> StartedNodes => [.. _startedNodes];

    /// <summary>Records that a scripted-model call for <paramref name="nodeId"/> has started.</summary>
    public void AddStarted(string nodeId) => _startedNodes.Enqueue(nodeId);

    /// <summary>Records a scripted-model call.</summary>
    public void Add(AgentCall call) => _agentCalls.Enqueue(call);

    /// <summary>Records a checkpoint.</summary>
    public void Add(RecordedSnapshot snapshot) => _snapshots.Enqueue(snapshot);

    /// <summary>Records an opened approval request.</summary>
    public void Add(ApprovalRequest approval) => _approvals.Enqueue(approval);

    /// <summary>Records an audit consolidation of <paramref name="input"/> on <paramref name="worker"/>.</summary>
    public void AddAudit(string worker, ConsolidateAuditInput input) => _audits.Enqueue(new RecordedAudit(input, worker, DateTimeOffset.UtcNow));
}
