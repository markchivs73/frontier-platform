using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>ADR-PA41 tests for <see cref="McpToolInvocationProjector"/>: an execution's MCP steps reach the signed audit with their contract hashes (ADR-E27 D15).</summary>
public sealed class McpToolInvocationProjectorTests
{
    private const string InputContractHash = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private const string OutputContractHash = "sha256:2222222222222222222222222222222222222222222222222222222222222222";

    internal static StepCompletion McpStep() => new()
    {
        NodeId = "t-tool",
        NodeType = NodeType.McpTool,
        ArtifactKey = "ticket-update",
        CorrelationId = "exec-1::t-tool::0",
        OutputContractType = "acme/ticket/1.0",
        OutputHash = "out-hash",
        RetryCount = 0,
        CompletedAtUtc = new DateTime(2026, 1, 1, 0, 15, 0, DateTimeKind.Utc),
        InputContractHash = InputContractHash,
        OutputContractHash = OutputContractHash,
        ToolRef = "com.example.crm/tickets/update_ticket",
        InputContractType = "acme/ticket-update/1.0",
        InputHash = "args-hash",
        Simulated = true,
    };

    [Fact]
    public void ToMcpToolInvocation_CopiesEveryAuditedField()
    {
        var step = McpStep();

        var invocation = McpToolInvocationProjector.ToMcpToolInvocation(step);

        Assert.Equal(new McpToolInvocation
        {
            CorrelationId = step.CorrelationId,
            NodeId = "t-tool",
            ArtifactKey = "ticket-update",
            ToolRef = "com.example.crm/tickets/update_ticket",
            InputContractType = "acme/ticket-update/1.0",
            InputHash = "args-hash",
            OutputContractType = "acme/ticket/1.0",
            OutputHash = "out-hash",
            InputContractHash = InputContractHash,
            OutputContractHash = OutputContractHash,
            Simulated = true,
            CompletedAtUtc = step.CompletedAtUtc,
        }, invocation);
    }

    /// <summary>A node naming no output contract records an empty name on its step; the audit leaves it out rather than recording "".</summary>
    [Fact]
    public void ToMcpToolInvocation_NoOutputContract_IsNull()
    {
        var invocation = McpToolInvocationProjector.ToMcpToolInvocation(McpStep() with { OutputContractType = string.Empty, OutputContractHash = null });

        Assert.Null(invocation.OutputContractType);
        Assert.Null(invocation.OutputContractHash);
    }

    [Fact]
    public void Project_KeepsOnlyMcpStepsThatNameTheirTool_InRecordedOrder()
    {
        var agentStep = WorkflowEventProjectorTests.Snapshot().CompletedSteps[0];
        var legacyMcpStep = McpStep() with { NodeId = "legacy", ToolRef = null };
        var second = McpStep() with { NodeId = "t-tool-2", CorrelationId = "exec-1::t-tool-2::0" };

        var invocations = McpToolInvocationProjector.Project([agentStep, McpStep(), legacyMcpStep, second]);

        Assert.NotNull(invocations);
        Assert.Equal(["t-tool", "t-tool-2"], invocations.Select(i => i.NodeId));
    }

    /// <summary>An execution with no MCP step keeps its record's bytes: the field is null, so it is omitted.</summary>
    [Fact]
    public void Project_NoMcpStep_IsNull()
    {
        Assert.Null(McpToolInvocationProjector.Project(WorkflowEventProjectorTests.Snapshot().CompletedSteps));
        Assert.Null(McpToolInvocationProjector.Project([]));
    }
}
