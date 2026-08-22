namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class NodeTypeTests
{
    [Fact]
    public void List_Always_ReturnsAllEightValuesInDeclarationOrder()
    {
        Assert.Equal(
            [
                NodeType.AgentTask,
                NodeType.HumanGate,
                NodeType.Decision,
                NodeType.Parallel,
                NodeType.Loop,
                NodeType.McpTool,
                NodeType.ContextInjection,
                NodeType.CascadeCheck,
            ],
            NodeType.List);
    }

    [Theory]
    [InlineData("agent_task")]
    [InlineData("human_gate")]
    [InlineData("decision")]
    [InlineData("parallel")]
    [InlineData("loop")]
    [InlineData("mcp_tool")]
    [InlineData("context_injection")]
    [InlineData("cascade_check")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, NodeType.FromName(name).Name);
    }
}
