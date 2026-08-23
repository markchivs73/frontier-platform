namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// The instance-id format is invariant 3 (<c>{engagementId}::{workflowId}</c>), so a malformed
/// id is a programming error rather than a runtime condition — but it still has to fail
/// loudly. This gap only became visible when the interpreter moved to a per-assembly coverage
/// gate; the merged report it used to live in absorbed it.
/// </summary>
public sealed class ExecutionIdParserTests
{
    [Fact]
    public void Parse_WellFormedId_SplitsEngagementAndWorkflow()
    {
        var (engagementId, workflowId) = ExecutionIdParser.Parse("eng-1::wf-1");

        Assert.Equal("eng-1", engagementId);
        Assert.Equal("wf-1", workflowId);
    }

    [Theory]
    [InlineData("eng-1")]
    [InlineData("")]
    public void Parse_WithoutTheSeparator_Throws(string executionId)
    {
        var ex = Assert.Throws<ArgumentException>(() => ExecutionIdParser.Parse(executionId));

        Assert.Contains("engagementId", ex.Message, StringComparison.Ordinal);
    }
}
