using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class ExecutionSnapshotTests
{
    [Fact]
    public void Validate_WellFormedSnapshot_DoesNotThrow()
    {
        var snapshot = Snapshot() with { DefinitionVersion = 1, Sequence = 0, Status = ExecutionStatus.Running };

        snapshot.Validate();
    }

    [Fact]
    public void Validate_DefinitionVersionBelowOne_Throws()
    {
        var snapshot = Snapshot() with { DefinitionVersion = 0 };

        var exception = Assert.Throws<ContractViolationException>(snapshot.Validate);

        Assert.Contains("definition_version must be at least 1.", exception.Violations);
    }

    [Fact]
    public void Validate_NegativeSequence_Throws()
    {
        var snapshot = Snapshot() with { Sequence = -1 };

        var exception = Assert.Throws<ContractViolationException>(snapshot.Validate);

        Assert.Contains("sequence must not be negative.", exception.Violations);
    }

    [Fact]
    public void Validate_PausedAtGateWithoutGateId_Throws()
    {
        var snapshot = Snapshot() with { Status = ExecutionStatus.PausedAtGate, PausedAtGateId = null };

        var exception = Assert.Throws<ContractViolationException>(snapshot.Validate);

        Assert.Contains("paused_at_gate_id is required when status is paused_at_gate.", exception.Violations);
    }

    [Fact]
    public void Validate_PausedAtGateWithGateId_DoesNotThrow()
    {
        var snapshot = Snapshot() with { Status = ExecutionStatus.PausedAtGate, PausedAtGateId = "gate-1" };

        snapshot.Validate();
    }

    /// <summary>
    /// S9.45 and ADR-PA47 (doc 03 §9/§10, doc 19 §B3): a run that ended on a failure says what kind, whether
    /// its checkpoint is <c>failed</c> or the legacy <c>paused_on_failure</c>.
    /// </summary>
    [Theory]
    [InlineData("failed")]
    [InlineData("paused_on_failure")]
    public void Validate_EndedOnFailureWithoutClassification_Throws(string status)
    {
        var snapshot = Snapshot() with { Status = ExecutionStatus.FromName(status), FailureClassification = null };

        var exception = Assert.Throws<ContractViolationException>(snapshot.Validate);

        Assert.Contains($"failure_classification is required when status is {status}.", exception.Violations);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("paused_on_failure")]
    public void Validate_EndedOnFailureWithClassification_DoesNotThrow(string status)
    {
        var snapshot = Snapshot() with { Status = ExecutionStatus.FromName(status), FailureClassification = "contract_violation" };

        snapshot.Validate();
    }

    static ExecutionSnapshot Snapshot() => new()
    {
        ExecutionId = "eng-1::wf-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        Sequence = 0,
        Status = ExecutionStatus.Running,
        CurrentNodeId = "node-1",
        Artifacts = new Dictionary<string, ArtifactStatus>(),
        CompletedSteps = [],
        Decisions = [],
        ApprovedSnapshotRefs = new Dictionary<string, string>(),
        CheckpointedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };
}
