
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Orchestration;
using Frontier.TestSupport;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S5.4 tests for <see cref="AuditConsolidator"/> (doc 05 §4 step 4).</summary>
public sealed class AuditConsolidatorTests
{
    private static readonly ConsolidateAuditInput Input = new()
    {
        ExecutionId = "eng-1::wf-1",
        DefinitionHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
        StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task ConsolidateAsync_NullInput_ThrowsArgumentNullException()
    {
        var consolidator = new AuditConsolidator(new FakeExecutionSnapshotReader(null), new FakeAuditTelemetryStaging([]));

        await Assert.ThrowsAsync<ArgumentNullException>(() => consolidator.ConsolidateAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ConsolidateAsync_NoSnapshotFound_ThrowsInvalidOperationException()
    {
        var consolidator = new AuditConsolidator(new FakeExecutionSnapshotReader(null), new FakeAuditTelemetryStaging([]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => consolidator.ConsolidateAsync(Input, CancellationToken.None));

        Assert.Contains(Input.ExecutionId, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConsolidateAsync_BuildsAuditRecordFromSnapshotAndTelemetry()
    {
        var snapshot = WorkflowEventProjectorTests.Snapshot();
        var telemetry = TelemetrySamples.Record() with { ExecutionId = snapshot.ExecutionId };
        var consolidator = new AuditConsolidator(new FakeExecutionSnapshotReader(snapshot), new FakeAuditTelemetryStaging([telemetry]));

        var record = await consolidator.ConsolidateAsync(Input, CancellationToken.None);

        Assert.Equal(snapshot.ExecutionId, record.ExecutionId);
        Assert.Equal(snapshot.EngagementId, record.EngagementId);
        Assert.Equal(snapshot.WorkflowId, record.WorkflowId);
        Assert.Equal(snapshot.DefinitionVersion, record.DefinitionVersion);
        Assert.Equal(Input.DefinitionHash, record.DefinitionHash);
        Assert.Equal(Input.StartedAtUtc, record.StartedAtUtc);
        Assert.Equal(snapshot.CheckpointedAtUtc, record.ClosedAtUtc);
        Assert.Equal(snapshot.Status, record.FinalStatus);
        Assert.Equal(WorkflowEventProjector.Project(snapshot, Input.StartedAtUtc), record.OrchestrationEvents);
        Assert.Equal(AgentInvocationProjector.Project([telemetry]), record.AgentInvocations);
        Assert.Empty(record.ValidatorOutcomes);
        Assert.Equal(HumanDecisionProjector.Project(snapshot.Decisions), record.HumanDecisions);
        Assert.Equal(CacheMetricsAggregator.Aggregate([telemetry]), record.CacheMetrics);
    }

    [Fact]
    public void BuildAuditRecord_AlwaysSetsEmptyValidatorOutcomes()
    {
        var snapshot = WorkflowEventProjectorTests.Snapshot();

        var record = AuditConsolidator.BuildAuditRecord(Input, snapshot.EngagementId, snapshot.WorkflowId, snapshot, []);

        Assert.Empty(record.ValidatorOutcomes);
    }

    [Fact]
    public void BuildAuditRecord_SandboxEngagementId_SetsSandboxTrue()
    {
        var snapshot = WorkflowEventProjectorTests.Snapshot();

        var record = AuditConsolidator.BuildAuditRecord(Input, "SANDBOX-abc123", snapshot.WorkflowId, snapshot, []);

        Assert.True(record.Sandbox);
    }

    [Fact]
    public void BuildAuditRecord_RealEngagementId_LeavesSandboxNull()
    {
        var snapshot = WorkflowEventProjectorTests.Snapshot();

        var record = AuditConsolidator.BuildAuditRecord(Input, snapshot.EngagementId, snapshot.WorkflowId, snapshot, []);

        Assert.Null(record.Sandbox);
    }
}
