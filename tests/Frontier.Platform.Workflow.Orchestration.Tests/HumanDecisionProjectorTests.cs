using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S5.4 tests for <see cref="HumanDecisionProjector"/> (doc 05 §4 step 4).</summary>
public sealed class HumanDecisionProjectorTests
{
    [Fact]
    public void ToHumanDecisionRecord_CopiesFieldsAndDropsRollbackToNodeId()
    {
        var decision = new HitlDecision
        {
            GateId = "human-gate",
            RequestId = "eng-1::wf-1:human-gate:1",
            ApproverId = "approver-1",
            Kind = DecisionKind.Reject,
            Notes = "Needs more detail.",
            RollbackToNodeId = "scope-agent",
            DecidedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
        };

        var record = HumanDecisionProjector.ToHumanDecisionRecord(decision);

        Assert.Equal(decision.GateId, record.GateId);
        Assert.Equal(decision.RequestId, record.RequestId);
        Assert.Equal(decision.ApproverId, record.ApproverId);
        Assert.Equal(decision.Kind, record.Kind);
        Assert.Equal(decision.Notes, record.Notes);
        Assert.Equal(decision.DecidedAtUtc, record.DecidedAtUtc);
    }

    [Fact]
    public void Project_MapsEveryDecision()
    {
        var first = WorkflowEventProjectorTests.Snapshot().Decisions[0];
        var second = first with { GateId = "second-gate", RequestId = "eng-1::wf-1:second-gate:1" };

        var records = HumanDecisionProjector.Project([first, second]);

        Assert.Equal(2, records.Count);
        Assert.Equal("human-gate", records[0].GateId);
        Assert.Equal("second-gate", records[1].GateId);
    }

    [Fact]
    public void Project_NoDecisions_ReturnsEmpty()
    {
        var records = HumanDecisionProjector.Project([]);

        Assert.Empty(records);
    }
}
