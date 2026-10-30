using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>ADR-E27 D13 and D14: a gate's decision is its node output, and a data edge's projection shapes the next node's input.</summary>
public sealed class EdgeProjectionAndGateOutputTests
{
    private const string Ticket = """{"ticket":{"id":"T-1","tags":["a","b"]},"a/b":1,"m~n":2}""";

    [Fact]
    public void Apply_BuildsExactlyTheProjectedFields_InDeclaredOrder()
    {
        var projected = EdgeProjector.Apply(Ticket,
        [
            new ProjectionField { Target = "ticket_id", From = "/ticket/id" },
            new ProjectionField { Target = "channel", Literal = Json("\"email\"") },
            new ProjectionField { Target = "first_tag", From = "/ticket/tags/0" },
            new ProjectionField { Target = "slash", From = "/a~1b" },
            new ProjectionField { Target = "tilde", From = "/m~0n" },
            new ProjectionField { Target = "whole", From = "" },
        ]);

        Assert.Equal(
            """{"ticket_id":"T-1","channel":"email","first_tag":"a","slash":1,"tilde":2,"whole":{"ticket":{"id":"T-1","tags":["a","b"]},"a/b":1,"m~n":2}}""",
            projected);
    }

    [Theory]
    [InlineData("/missing")]
    [InlineData("/ticket/id/deeper")]
    [InlineData("/ticket/tags/5")]
    [InlineData("/ticket/tags/x")]
    public void Apply_LeavesOutATargetWhosePointerFindsNothing(string location)
    {
        Assert.Equal("{}", EdgeProjector.Apply(Ticket, [new ProjectionField { Target = "t", From = location }]));
    }

    [Fact]
    public void ResolveUpstreamPayloadForNode_AppliesTheEdgesProjection_AndPassesAnUnprojectedPayloadThrough()
    {
        var state = new GraphExecutionState { StartedAtUtc = OrchestrationFixtures.StartedAtUtc };
        state.NodeOutputPayloads["p"] = Ticket;
        var projected = Definition(new ProjectionField { Target = "ticket_id", From = "/ticket/id" });
        var plain = Definition(null);

        Assert.Equal("""{"ticket_id":"T-1"}""", GraphOrchestratorSteps.ResolveUpstreamPayloadForNode(projected, "c", state));
        Assert.Equal(Ticket, GraphOrchestratorSteps.ResolveUpstreamPayloadForNode(plain, "c", state));
        Assert.Null(GraphOrchestratorSteps.ResolveUpstreamPayloadForNode(plain, "p", state));
    }

    [Fact]
    public void GateOutput_IsTheDecisionAsTheGateDecisionContract()
    {
        var decision = new HitlDecision
        {
            GateId = "gate-1",
            RequestId = "r",
            ApproverId = "user:approver-1",
            Kind = DecisionKind.Approve,
            Notes = "ok",
            DecidedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var output = GraphOrchestratorSteps.GateOutput(decision);

        Assert.Equal("""{"schema_version":"1.0","gate_id":"gate-1","decision":"approve","approver_id":"user:approver-1","decided_at_utc":"2026-01-01T00:00:00.000Z","notes":"ok"}""", output);
        Assert.True(TestContractValidator.Instance.Validate(EngineContracts.GateDecision, System.Text.Encoding.UTF8.GetBytes(output)).IsValid);
    }

    private static WorkflowDefinition Definition(ProjectionField? field) => new()
    {
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        EngagementType = "t",
        Name = "n",
        Nodes = [],
        Edges = [new WorkflowEdge { FromNodeId = "p", ToNodeId = "c", Kind = EdgeKind.Data, ContractType = "x/y/1.0", Projection = field is null ? null : [field] }],
        DefinitionHash = "sha256:x",
        Mode = ExecutionMode.OneShot,
    };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
