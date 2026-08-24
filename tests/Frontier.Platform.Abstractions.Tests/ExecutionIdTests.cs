using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

/// <summary>
/// Invariant 3's format, tested once. This suite consolidates two identical copies that had grown
/// in <c>Audit.Tests</c> and <c>Workflow.Orchestration.Tests</c> — the same split that produced the
/// duplicate helpers they covered.
/// </summary>
public sealed class ExecutionIdTests
{
    [Fact]
    public void Mint_JoinsTheSegmentsWithTheSeparator()
    {
        Assert.Equal("eng-1::wf-1", ExecutionId.Mint("eng-1", "wf-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Mint_WithAMissingSegment_Throws(string? engagementId)
    {
        // ThrowsAny: null yields ArgumentNullException, blank yields ArgumentException.
        var ex = Assert.ThrowsAny<ArgumentException>(() => ExecutionId.Mint(engagementId!, "wf-1"));

        Assert.Equal("engagementId", ex.ParamName);
    }

    /// <summary>
    /// A segment containing the separator would produce an id that still looks well-formed but
    /// parses back to different values than were minted — silent, and only visible downstream.
    /// </summary>
    [Fact]
    public void Mint_WithASegmentContainingTheSeparator_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => ExecutionId.Mint("eng-1", "wf::1"));

        Assert.Equal("workflowId", ex.ParamName);
        Assert.Contains("separator", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MintAndParse_RoundTrip()
    {
        var (engagementId, workflowId) = ExecutionId.Parse(ExecutionId.Mint("eng-1", "wf-1"));

        Assert.Equal("eng-1", engagementId);
        Assert.Equal("wf-1", workflowId);
    }

    /// <summary>A dispatcher child appends <c>::{workItemId}</c> and resolves to its parent's pair.</summary>
    [Fact]
    public void Parse_DispatcherChildId_IgnoresTheThirdSegment()
    {
        var (engagementId, workflowId) = ExecutionId.Parse("eng-1::wf-1::item-1");

        Assert.Equal("eng-1", engagementId);
        Assert.Equal("wf-1", workflowId);
    }

    [Theory]
    [InlineData("eng-1")]
    [InlineData("")]
    public void Parse_WithoutTheSeparator_Throws(string executionId)
    {
        var ex = Assert.Throws<ArgumentException>(() => ExecutionId.Parse(executionId));

        Assert.Equal("executionId", ex.ParamName);
        Assert.Contains("engagementId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOrNull_WellFormedId_ReturnsTheSegments()
    {
        Assert.Equal(("eng-1", "wf-1"), ExecutionId.ParseOrNull("eng-1::wf-1"));
    }

    /// <summary>
    /// The tolerant reading exists for callers holding an identifier that may legitimately be
    /// something else, so a malformed value is a branch rather than an exception.
    /// </summary>
    [Theory]
    [InlineData("eng-1")]
    [InlineData("")]
    public void ParseOrNull_WithoutTheSeparator_ReturnsNull(string executionId)
    {
        Assert.Null(ExecutionId.ParseOrNull(executionId));
    }

    [Fact]
    public void Separator_IsTheDocumentedFormat()
    {
        Assert.Equal("::", ExecutionId.Separator);
    }
}
