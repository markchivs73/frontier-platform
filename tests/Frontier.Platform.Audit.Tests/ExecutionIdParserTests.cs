namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.4 tests for <see cref="ExecutionIdParser"/> (rule 3).</summary>
public sealed class ExecutionIdParserTests
{
    [Fact]
    public void Parse_TopLevelExecutionId_ReturnsEngagementAndWorkflow()
    {
        var (engagementId, workflowId) = ExecutionIdParser.Parse("eng-1::wf-1");

        Assert.Equal("eng-1", engagementId);
        Assert.Equal("wf-1", workflowId);
    }

    [Fact]
    public void Parse_DispatcherChildExecutionId_IgnoresThirdSegment()
    {
        var (engagementId, workflowId) = ExecutionIdParser.Parse("eng-1::wf-1::item-1");

        Assert.Equal("eng-1", engagementId);
        Assert.Equal("wf-1", workflowId);
    }

    [Fact]
    public void Parse_MissingSeparator_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => ExecutionIdParser.Parse("eng-1"));

        Assert.Equal("executionId", ex.ParamName);
    }
}
