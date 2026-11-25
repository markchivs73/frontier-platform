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

    /// <summary>ADR-PA53: the decision's authority reaches the signed record; a decision with none projects none.</summary>
    [Fact]
    public void ToHumanDecisionRecord_CopiesTheAuthority_AndNoneStaysNone()
    {
        var plain = WorkflowEventProjectorTests.Snapshot().Decisions[0];
        var authority = new DecisionAuthority { RoleId = "finance-lead", Scope = new DecisionScope { Kind = "engagement", Id = "eng-1" } };

        Assert.Equal(authority, HumanDecisionProjector.ToHumanDecisionRecord(plain with { Authority = authority }).Authority);
        Assert.Null(HumanDecisionProjector.ToHumanDecisionRecord(plain).Authority);
    }

    /// <summary>
    /// ADR-PA53: a gate event recorded in DTF history before the member existed deserializes with no authority, so a
    /// replay reads the same decision; a newer payload read by this contract keeps it.
    /// </summary>
    [Fact]
    public void RecordedGateEvent_WithoutTheAuthority_DeserializesToNone_AndWithIt_KeepsIt()
    {
        const string recorded = """{"gate_id":"human-gate","request_id":"exec-1:human-gate:1","approver_id":"approver-1","kind":"approve","decided_at_utc":"2026-01-01T01:00:00.000Z"}""";
        const string newer = """{"gate_id":"human-gate","request_id":"exec-1:human-gate:1","approver_id":"approver-1","kind":"approve","decided_at_utc":"2026-01-01T01:00:00.000Z","authority":{"role_id":"finance-lead","scope":{"kind":"deployment"}}}""";

        var old = System.Text.Json.JsonSerializer.Deserialize<HitlDecision>(recorded, Frontier.Platform.Serialization.CanonicalProfile.Options)!;
        var current = System.Text.Json.JsonSerializer.Deserialize<HitlDecision>(newer, Frontier.Platform.Serialization.CanonicalProfile.Options)!;

        Assert.Null(old.Authority);
        Assert.Equal(recorded, System.Text.Encoding.UTF8.GetString(Frontier.Platform.Serialization.CanonicalProfile.SerializeCanonical(old)));
        Assert.Equal(new DecisionAuthority { RoleId = "finance-lead", Scope = new DecisionScope { Kind = "deployment" } }, current.Authority);
        Assert.Equal(newer, System.Text.Encoding.UTF8.GetString(Frontier.Platform.Serialization.CanonicalProfile.SerializeCanonical(current)));
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
