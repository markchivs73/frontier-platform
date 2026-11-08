using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.240 (ADR-PA46): <see cref="AuditConsolidator"/> closes completed, failed and terminated runs, each
/// with its true final status and the fields that say why it ended, and refuses to attest an outcome
/// its snapshot does not record.
/// </summary>
public sealed class AuditOutcomeConsolidationTests
{
    private static readonly DateTime StartedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TerminatedAtUtc = new(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);

    private static readonly ExecutionFailureDetail Detail = new()
    {
        NodeId = "scope-agent",
        ExceptionType = "Frontier.Platform.Abstractions.ContractViolationException",
        Message = "SummaryArtifact: reply failed validation.",
    };

    [Fact]
    public async Task ConsolidateAsync_CompletedRun_AttestsCompletedWithNoOutcomeMembers()
    {
        var record = await ConsolidateAsync(WorkflowEventProjectorTests.Snapshot(), finalStatus: null);

        Assert.Equal(ExecutionStatus.Completed, record.FinalStatus);
        Assert.Null(record.Failure);
        Assert.Null(record.Termination);
        Assert.Equal("completed", record.OrchestrationEvents[^1].Details);
    }

    /// <summary>The failure path: the checkpoint says paused_on_failure; the record attests failed and says why.</summary>
    [Fact]
    public async Task ConsolidateAsync_FailedRun_AttestsFailedOverThePausedCheckpointWithTheFailure()
    {
        var record = await ConsolidateAsync(PausedOnFailure(Detail), ExecutionStatus.Failed);

        Assert.Equal(ExecutionStatus.Failed, record.FinalStatus);
        Assert.Equal(new FailureRecord { Classification = "contract_violation", NodeId = Detail.NodeId, ExceptionType = Detail.ExceptionType, Message = Detail.Message }, record.Failure);
        Assert.Null(record.Termination);
        Assert.Equal("failed", record.OrchestrationEvents[^1].Details);
        record.Validate();
    }

    /// <summary>A checkpoint with a classification but no detail (a consumer-healed one, say) still says what kind of failure.</summary>
    [Fact]
    public async Task ConsolidateAsync_FailedRunWithoutDetail_CarriesTheClassificationAlone()
    {
        var record = await ConsolidateAsync(PausedOnFailure(detail: null), ExecutionStatus.Failed);

        Assert.Equal(new FailureRecord { Classification = "contract_violation" }, record.Failure);
    }

    /// <summary>The consumer's terminate: the terminated snapshot is written first, then consolidated.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConsolidateAsync_TerminatedRun_AttestsTerminatedWithWhoWhyAndWhen(bool attestExplicitly)
    {
        var record = await ConsolidateAsync(Terminated(), attestExplicitly ? ExecutionStatus.Terminated : null);

        Assert.Equal(ExecutionStatus.Terminated, record.FinalStatus);
        Assert.Equal(new TerminationRecord { TerminatedBy = "user:operator-1", Reason = "Parked for good.", TerminatedAtUtc = TerminatedAtUtc }, record.Termination);
        Assert.Equal("terminated", record.OrchestrationEvents[^1].Details);
        record.Validate();
    }

    /// <summary>A run that paused on a failure and was then terminated keeps both facts.</summary>
    [Fact]
    public async Task ConsolidateAsync_TerminatedAfterAFailure_CarriesBoth()
    {
        var record = await ConsolidateAsync(Terminated() with { FailureClassification = "guardrail" }, finalStatus: null);

        Assert.Equal("guardrail", record.Failure!.Classification);
        Assert.NotNull(record.Termination);
    }

    [Fact]
    public async Task ConsolidateAsync_AttestedFailedButTheSnapshotRecordsNoFailure_IsAContractViolation()
    {
        var ex = await Assert.ThrowsAsync<ContractViolationException>(() => ConsolidateAsync(WorkflowEventProjectorTests.Snapshot(), ExecutionStatus.Failed));

        Assert.Contains("failure_classification", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A terminate that consolidates before it writes the terminated snapshot is refused rather than signed.</summary>
    [Fact]
    public async Task ConsolidateAsync_AttestedTerminatedBeforeTheTerminatedSnapshot_IsAContractViolation()
    {
        var gatePaused = WorkflowEventProjectorTests.Snapshot() with { Status = ExecutionStatus.PausedAtGate };

        var ex = await Assert.ThrowsAsync<ContractViolationException>(() => ConsolidateAsync(gatePaused, ExecutionStatus.Terminated));

        Assert.Contains("write the terminated snapshot before consolidating", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "why", true)]
    [InlineData("who", null, true)]
    [InlineData("who", "why", false)]
    public void Termination_AnyOfTheThreeFieldsMissing_IsNull(string? terminatedBy, string? reason, bool withTime)
    {
        var snapshot = Terminated() with { TerminatedBy = terminatedBy, TerminationReason = reason, TerminatedAtUtc = withTime ? TerminatedAtUtc : null };

        Assert.Null(AuditOutcomeProjector.Termination(snapshot));
    }

    [Fact]
    public void Failure_NoClassification_IsNull() =>
        Assert.Null(AuditOutcomeProjector.Failure(WorkflowEventProjectorTests.Snapshot() with { FailureDetail = Detail }));

    private static Task<AuditRecord> ConsolidateAsync(ExecutionSnapshot snapshot, ExecutionStatus? finalStatus)
    {
        var consolidator = new AuditConsolidator(new FakeExecutionSnapshotReader(snapshot), new FakeAuditTelemetryStaging([]));
        return consolidator.ConsolidateAsync(new ConsolidateAuditInput
        {
            ExecutionId = snapshot.ExecutionId,
            EngagementId = snapshot.EngagementId,
            WorkflowId = snapshot.WorkflowId,
            DefinitionHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
            StartedAtUtc = StartedAtUtc,
            FinalStatus = finalStatus,
        }, CancellationToken.None);
    }

    private static ExecutionSnapshot PausedOnFailure(ExecutionFailureDetail? detail) => WorkflowEventProjectorTests.Snapshot() with
    {
        Status = ExecutionStatus.PausedOnFailure,
        CurrentNodeId = "scope-agent",
        FailureClassification = "contract_violation",
        FailureDetail = detail,
    };

    private static ExecutionSnapshot Terminated() => WorkflowEventProjectorTests.Snapshot() with
    {
        Status = ExecutionStatus.Terminated,
        TerminatedBy = "user:operator-1",
        TerminationReason = "Parked for good.",
        TerminatedAtUtc = TerminatedAtUtc,
    };
}
