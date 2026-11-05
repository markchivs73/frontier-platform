using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Hitl;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests.Replay;

/// <summary>
/// The store-shaped activities a consumer supplies (snapshot writer, artifact state, approval store,
/// audit consolidation, dynamic-context refresh), reduced to recording what they received (S13.192). The snapshot write can be
/// held for <paramref name="snapshotDelay"/> so a checkpoint is still in flight when a sibling branch
/// completes, the review-01 ENG-02 interleaving.
/// </summary>
internal sealed class RecordingActivities(string worker, ReplayRecorder recorder, TimeSpan snapshotDelay)
{
    /// <summary>Records <paramref name="snapshot"/> after the scripted write delay.</summary>
    public async Task<SnapshotActivityResponse> WriteSnapshotAsync(ExecutionSnapshot snapshot)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await Task.Delay(snapshotDelay);
        recorder.Add(new RecordedSnapshot(snapshot, worker, startedAt, DateTimeOffset.UtcNow));
        return new SnapshotActivityResponse { SnapshotId = $"{snapshot.ExecutionId}:{snapshot.Sequence:D6}" };
    }

    /// <summary>Answers an artifact-state append with a stable section ref.</summary>
    public static ArtifactStateActivityResponse WriteArtifact(ArtifactStateActivityRequest request) =>
        new() { SectionRef = $"{request.ExecutionId}:{request.ArtifactKey}:v{request.Version}" };

    /// <summary>Applies a dynamic-context refresh: the new epoch is the count of refreshes applied so far.</summary>
    public DynamicContextRefreshResult Refresh(RefreshDynamicContextRequest request)
    {
        var epoch = recorder.AddRefresh(request.Reason);
        return new DynamicContextRefreshResult(Refreshed: true, Epoch: epoch, ContentHash: $"hash-{epoch}");
    }

    /// <summary>Opens and records the approval request for <paramref name="request"/>.</summary>
    public ApprovalRequest OpenGate(GateOpenRequest request)
    {
        var approval = ApprovalRequestFactory.Open(request);
        recorder.Add(approval);
        return approval;
    }

    /// <summary>
    /// Records the consolidation and answers with a fixture-signed record. The engagement and
    /// workflow come from the input's typed fields: the instance id here is an opaque run token, so
    /// <see cref="AuditFixtures.SignedRecord(ConsolidateAuditInput)"/>, which splits it, does not apply.
    /// </summary>
    public SignedAuditRecord Consolidate(ConsolidateAuditInput input)
    {
        recorder.AddAudit(worker);
        return AuditFixtures.SignedRecord(new AuditRecord
        {
            ExecutionId = input.ExecutionId,
            EngagementId = input.EngagementId,
            WorkflowId = input.WorkflowId,
            DefinitionVersion = 1,
            DefinitionHash = input.DefinitionHash,
            StartedAtUtc = input.StartedAtUtc,
            ClosedAtUtc = input.StartedAtUtc,
            FinalStatus = ExecutionStatus.Completed,
            OrchestrationEvents = [],
            AgentInvocations = [],
            ValidatorOutcomes = [],
            HumanDecisions = [],
            CacheMetrics = AuditFixtures.EmptyCacheMetrics(),
        });
    }
}
