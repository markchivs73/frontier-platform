using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Rules;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.7i (ADR-5 Decision 3): <c>data.single-data-predecessor</c> — every node has at
/// most one inbound Data edge, because the runtime delivers exactly one upstream payload
/// and fan-in converges via Control edges.
/// </summary>
public sealed class S137iSchedulerRuleTests
{
    [Fact]
    public async Task NoDataEdges_ReturnsEmpty()
    {
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("agent-1"), S930Fixtures.Agent("agent-2")],
            edges: [S930Fixtures.Control("agent-1", "agent-2")]);

        Assert.Empty(await new DataSingleDataPredecessorRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
    }

    [Fact]
    public async Task SingleDataPredecessorPerNode_ReturnsEmpty()
    {
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("agent-1"), S930Fixtures.Agent("agent-2"), S930Fixtures.Agent("agent-3")],
            edges:
            [
                S930Fixtures.Data("agent-1", "agent-2", "SummaryArtifact"),
                S930Fixtures.Data("agent-2", "agent-3", "PlanArtifact"),
            ]);

        Assert.Empty(await new DataSingleDataPredecessorRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
    }

    [Fact]
    public async Task TwoDataPredecessors_ReturnsFindingNamingBothSources()
    {
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("agent-1"), S930Fixtures.Agent("agent-2"), S930Fixtures.Agent("agent-join")],
            edges:
            [
                S930Fixtures.Data("agent-1", "agent-join", "SummaryArtifact"),
                S930Fixtures.Data("agent-2", "agent-join", "PlanArtifact"),
            ]);

        var finding = Assert.Single(await new DataSingleDataPredecessorRule().EvaluateAsync(Ctx(definition), CancellationToken.None));

        Assert.Equal("data.single-data-predecessor", finding.RuleId);
        Assert.Equal("agent-join", finding.NodeId);
        Assert.Contains("'agent-1'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("'agent-2'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("Control edges", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ControlFanInWithSingleDataEdge_ReturnsEmpty()
    {
        // The recommended convergence shape: branches join via Control edges; the join
        // declares exactly one Data-edge predecessor.
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("agent-1"), S930Fixtures.Agent("agent-2"), S930Fixtures.Agent("agent-join")],
            edges:
            [
                S930Fixtures.Control("agent-1", "agent-join"),
                S930Fixtures.Control("agent-2", "agent-join"),
                S930Fixtures.Data("agent-1", "agent-join", "SummaryArtifact"),
            ]);

        Assert.Empty(await new DataSingleDataPredecessorRule().EvaluateAsync(Ctx(definition), CancellationToken.None));
    }

    private static DefinitionValidationContext Ctx(WorkflowDefinition definition) => new(definition);
}
