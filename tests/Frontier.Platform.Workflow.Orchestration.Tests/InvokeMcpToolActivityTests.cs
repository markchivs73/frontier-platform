namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S13.7c: the thin activity wrapper delegates to its pipeline (the AgentTaskActivity pattern).</summary>
public sealed class InvokeMcpToolActivityTests
{
    [Fact]
    public async Task RunAsync_DelegatesToThePipeline()
    {
        McpToolActivityInput? seen = null;
        var expected = new McpToolActivityResult
        {
            NodeId = "t-1",
            ArtifactKey = null,
            ToolRef = "io.frontier.demo/autotask/get_new_ticket",
            OutputPayload = "{}",
            OutputHash = "hash",
            Simulated = false,
            HostBuild = "build",
        };
        var activity = new InvokeMcpToolActivity(new StubPipeline(input => { seen = input; return expected; }));
        var input = new McpToolActivityInput
        {
            NodeId = "t-1",
            ToolRef = "io.frontier.demo/autotask/get_new_ticket",
            TimeoutSeconds = 30,
            CorrelationId = "c-1",
            ExecutionId = "eng-1::wf",
            EngagementId = "eng-1",
        };

        var result = await activity.RunAsync(new FakeTaskActivityContext(), input);

        Assert.Same(expected, result);
        Assert.Same(input, seen);
    }

    [Fact]
    public void Constructor_NullPipeline_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new InvokeMcpToolActivity(null!));
    }

    private sealed class StubPipeline(Func<McpToolActivityInput, McpToolActivityResult> implementation) : IMcpToolInvocationPipeline
    {
        public Task<McpToolActivityResult> RunAsync(McpToolActivityInput input, CancellationToken ct) =>
            Task.FromResult(implementation(input));
    }
}
