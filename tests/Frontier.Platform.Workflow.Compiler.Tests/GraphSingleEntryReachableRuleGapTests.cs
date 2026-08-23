using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Rules;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>S9.24: two <see cref="GraphSingleEntryReachableRule"/> branches the existing suite didn't reach: an empty node list, and an unreachable node behind a single valid entry.</summary>
public sealed class GraphSingleEntryReachableRuleGapTests
{
    private static ContextRequest ContextRequest() => new()
    {
        EngagementId = "eng-1",
        AgentRole = "deep-reasoning",
        BaselineComponents = [],
        DynamicFields = [],
    };

    private static AgentTaskNode Agent(string id) => new()
    {
        NodeId = id,
        Role = "deep-reasoning",
        InstructionsRef = "instructions.md",
        InputContractType = "In",
        OutputContractType = "Out",
        ContextRequest = ContextRequest(),
    };

    private static WorkflowDefinition Definition(IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges) => new()
    {
        WorkflowId = "wf-test",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Test",
        Nodes = nodes,
        Edges = edges,
        DefinitionHash = "hash",
        Mode = ExecutionMode.OneShot,
    };

    [Fact]
    public async Task EvaluateAsync_EmptyNodeList_ReturnsNoFindings()
    {
        var rule = new GraphSingleEntryReachableRule();
        var ctx = new DefinitionValidationContext(Definition([], []));

        var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task EvaluateAsync_SingleEntryWithUnreachableCycle_FlagsBothOrphanedNodes()
    {
        // A 2-node cycle (mid<->orphan) gives both nodes an incoming edge — so neither
        // becomes a second "entry" — while remaining disconnected from the real entry node.
        var rule = new GraphSingleEntryReachableRule();
        var definition = Definition(
            [Agent("entry"), Agent("reachable"), Agent("mid"), Agent("orphan")],
            [
                new WorkflowEdge { FromNodeId = "entry", ToNodeId = "reachable", Kind = EdgeKind.Control },
                new WorkflowEdge { FromNodeId = "mid", ToNodeId = "orphan", Kind = EdgeKind.Control },
                new WorkflowEdge { FromNodeId = "orphan", ToNodeId = "mid", Kind = EdgeKind.Control },
            ]);
        var ctx = new DefinitionValidationContext(definition);

        var findings = await rule.EvaluateAsync(ctx, CancellationToken.None);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Contains("unreachable", f.Message, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.NodeId == "mid");
        Assert.Contains(findings, f => f.NodeId == "orphan");
    }
}
